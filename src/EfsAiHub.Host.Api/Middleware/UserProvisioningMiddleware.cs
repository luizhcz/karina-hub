using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Configuration;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Auto-cadastra o usuário no diretório (tabela aihub.users) no primeiro
/// request e popula <see cref="IUserContextAccessor"/> +
/// <see cref="IRequestAuthContextAccessor"/> pra downstream (AdminGate,
/// controllers, audit, GenericToolExecutor). Cache em memória de 60s por
/// (TenantId, ExternalUserId) evita martelar o DB com upserts redundantes.
///
/// Headers consumidos:
///   - <c>x-efs-account</c> / <c>x-efs-user-profile-id</c> → identidade
///     (resolvida via <see cref="IUserIdentityProvider"/>).
///   - <c>x-efs-permissions</c> → CSV de permissões resolvidas pelo proxy.
///     Obrigatório quando identidade é fornecida (vazio é válido — autenticado
///     sem permissão). Ausente quando identidade existe → 400 BadRequest.
///   - <c>app_origin</c> → canal do caller (ex.: web-mvp). Pass-through.
///   - <c>access_token</c> → token opaco do IdP do consumidor. Não é validado
///     aqui; serve como bearer pras chamadas downstream.
///
/// Sem identidade no request: middleware é no-op (rotas públicas como
/// /health/* continuam funcionando sem usuário associado). Falha de DB
/// também é no-op + log — auth não pode derrubar request path.
///
/// IsAdmin e Permissions do <see cref="User"/> retornado são populados pelo
/// <see cref="IAdminPermissionEvaluator"/> a cada request — inclusive em cache
/// hit, pra que mudança de permission no proxy reflita imediatamente sem
/// esperar TTL.
///
/// Deve rodar DEPOIS de TenantMiddleware (precisa do TenantId resolvido)
/// e ANTES de AdminGateMiddleware (que lê IUserContextAccessor.Current).
///
/// IMPORTANTE — rotas em <see cref="UserProvisioningOptions.SkipPathPrefixes"/>:
/// pra essas rotas o UPSERT é pulado e <see cref="IUserContextAccessor.Current"/>
/// fica null mesmo com headers de identidade presentes. Handlers que atendem
/// essas rotas precisam resolver identity por conta própria
/// (<see cref="IUserIdentityProvider"/> / <c>UserIdentityResolver.TryResolve</c>)
/// em vez de consumir <c>IUserContextAccessor.Current</c> — caso contrário vão
/// ver request anônimo. Default skip-list cobre o AG-UI stream, que já segue
/// esse padrão (resolve no próprio endpoint).
/// </summary>
public sealed class UserProvisioningMiddleware
{
    public const string AppOriginHeader = "app_origin";
    public const string AccessTokenHeader = "access_token";

    private readonly RequestDelegate _next;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public UserProvisioningMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IUserIdentityProvider identityProvider,
        IUserDirectory directory,
        IUserContextAccessor userAccessor,
        ITenantContextAccessor tenantAccessor,
        IRequestAuthContextAccessor authContext,
        IAdminPermissionEvaluator adminEvaluator,
        IMemoryCache cache,
        IAdminAuditLogger audit,
        IOptions<UserProvisioningOptions> options,
        ILogger<UserProvisioningMiddleware> logger)
    {
        // app_origin e access_token são populados independente de identidade
        // resolvida — generic-tools podem ser chamadas por agentes em contextos
        // sem x-efs-* (ex.: workflow disparado por scheduler), mas o downstream
        // ainda quer o canal/token original.
        authContext.AppOrigin = ReadFirstNonEmptyHeader(context.Request, AppOriginHeader);
        authContext.AccessToken = ReadFirstNonEmptyHeader(context.Request, AccessTokenHeader);

        var identity = identityProvider.Resolve(context, out var identityError);
        if (identity is null)
        {
            // Erro descritivo (ex.: x-efs-permissions ausente, headers ambíguos)
            // só vira 400 quando o caller enviou ALGUMA identidade — request
            // totalmente anônimo (sem nenhum header de auth) segue como public.
            if (!string.IsNullOrEmpty(identityError) && HasAnyIdentityInput(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { error = identityError });
                return;
            }
            await _next(context);
            return;
        }

        // Rotas externas (ex.: AG-UI trigger) trazem header de identidade só
        // pra audit/threads — não devem inflar aihub.users com consumers que
        // nunca vão usar a UI. Pula apenas o UPSERT; demais downstream segue
        // (AdminGate trata Current=null como non-admin, e a whitelist de rota
        // pública é aplicada no próprio AdminGate).
        if (ShouldSkipProvisioning(context.Request.Path, options.Value))
        {
            logger.LogDebug(
                "UserProvisioning skipped (path em SkipPathPrefixes) externalUserId={ExternalUserId} path={Path}",
                identity.ExternalUserId, context.Request.Path.Value);
            await _next(context);
            return;
        }

        var tenantId = tenantAccessor.Current.TenantId;
        var cacheKey = BuildCacheKey(tenantId, identity.ExternalUserId);

        User? user = null;
        try
        {
            user = await cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheTtl;
                var result = await directory.UpsertAsync(
                    identity.ExternalUserId,
                    identity.UserType,
                    tenantId,
                    displayName: null,
                    context.RequestAborted);

                if (result.Created)
                {
                    try
                    {
                        await audit.RecordAsync(new AdminAuditEntry
                        {
                            TenantId = tenantId,
                            ProjectId = null,
                            ActorUserId = "system:provisioning",
                            ActorUserType = AdminAuditActorTypes.System,
                            Action = AdminAuditActions.UserAutoProvisioned,
                            ResourceType = AdminAuditResources.User,
                            ResourceId = result.User.Id.ToString(),
                            PayloadAfter = AdminAuditContext.Snapshot(new
                            {
                                userId = result.User.Id,
                                externalUserId = result.User.ExternalUserId,
                                userType = result.User.UserType,
                                tenantId = result.User.TenantId,
                            }),
                            Timestamp = DateTime.UtcNow,
                        }, context.RequestAborted);
                    }
                    catch (Exception auditEx)
                    {
                        // Falha de audit não pode bloquear provisioning — segue.
                        logger.LogWarning(auditEx,
                            "Audit user.auto_provisioned falhou para userId={UserId}",
                            result.User.Id);
                    }
                }
                return result.User;
            });
        }
        catch (Exception ex)
        {
            // Falha de DB não pode derrubar request. Sem User no accessor,
            // AdminGate vai bloquear rotas admin (fail-closed pro privilégio).
            // Rotas públicas continuam normais.
            logger.LogWarning(ex,
                "UserProvisioning falhou para externalUserId={ExternalUserId} tenant={TenantId}",
                identity.ExternalUserId, tenantId);
        }

        if (user is not null)
        {
            // Permissions/IsAdmin vêm do header — sobrescreve o objeto cacheado
            // pra que mudança de papel no proxy reflita imediatamente, sem
            // esperar o TTL de 60s do diretório.
            userAccessor.Current = new User
            {
                Id = user.Id,
                ExternalUserId = user.ExternalUserId,
                UserType = user.UserType,
                TenantId = user.TenantId,
                DisplayName = user.DisplayName,
                CreatedAt = user.CreatedAt,
                LastSeenAt = user.LastSeenAt,
                Permissions = identity.Permissions,
                IsAdmin = adminEvaluator.IsAdmin(identity.Permissions),
            };
        }

        await _next(context);
    }

    /// <summary>
    /// Invalida a entrada de cache pra um par (tenant, externalUserId).
    /// Admin chama isso após mudar DisplayName via UI pra que a próxima
    /// request enxergue o estado novo sem esperar o TTL.
    /// </summary>
    public static void InvalidateCache(IMemoryCache cache, string tenantId, string externalUserId)
        => cache.Remove(BuildCacheKey(tenantId, externalUserId));

    private static string BuildCacheKey(string tenantId, string externalUserId)
        => $"user-provisioning:{tenantId}:{externalUserId}";

    private static bool HasAnyIdentityInput(HttpRequest request)
    {
        var account = request.Headers[UserIdentityResolver.Headers.Account].FirstOrDefault();
        var profileId = request.Headers[UserIdentityResolver.Headers.UserProfileId].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(account) || !string.IsNullOrWhiteSpace(profileId))
            return true;
        var accountQ = request.Query[UserIdentityResolver.QueryParams.Account].FirstOrDefault();
        var profileIdQ = request.Query[UserIdentityResolver.QueryParams.UserProfileId].FirstOrDefault();
        return !string.IsNullOrWhiteSpace(accountQ) || !string.IsNullOrWhiteSpace(profileIdQ);
    }

    private static bool ShouldSkipProvisioning(PathString requestPath, UserProvisioningOptions options)
    {
        if (!options.SkipAnonymousRoutes) return false;
        if (options.SkipPathPrefixes is not { Count: > 0 } prefixes) return false;
        var path = requestPath.Value;
        if (string.IsNullOrEmpty(path)) return false;
        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrEmpty(prefix)) continue;
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // Exige boundary de segmento: evita que prefixo "/api/aihub/chat/ag-ui"
            // case acidentalmente um path tipo "/api/aihub/chat/ag-uianything".
            if (path.Length == prefix.Length) return true;
            if (path[prefix.Length] == '/') return true;
        }
        return false;
    }

    private static string? ReadFirstNonEmptyHeader(HttpRequest request, string headerName)
    {
        if (!request.Headers.TryGetValue(headerName, out var values)) return null;
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }
}
