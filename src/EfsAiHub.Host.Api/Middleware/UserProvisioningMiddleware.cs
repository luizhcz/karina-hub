using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Caching.Memory;

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
///   - <c>app_origin</c> → canal do caller (ex.: web-mvp). Pass-through
///     pras chamadas downstream de generic-tools.
///   - <c>access_token</c> → token opaco do IdP do consumidor. Não é
///     validado aqui; serve como bearer pras chamadas downstream.
///
/// Sem identidade no request: middleware é no-op (rotas públicas como
/// /health/* continuam funcionando sem usuário associado). Falha de DB
/// também é no-op + log — auth não pode derrubar request path.
///
/// Deve rodar DEPOIS de TenantMiddleware (precisa do TenantId resolvido)
/// e ANTES de AdminGateMiddleware (que lê IUserContextAccessor.Current).
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
        IMemoryCache cache,
        IAdminAuditLogger audit,
        ILogger<UserProvisioningMiddleware> logger)
    {
        // app_origin e access_token são populados independente de identidade
        // resolvida — generic-tools podem ser chamadas por agentes em contextos
        // sem x-efs-* (ex.: workflow disparado por scheduler), mas o downstream
        // ainda quer o canal/token original.
        authContext.AppOrigin = ReadFirstNonEmptyHeader(context.Request, AppOriginHeader);
        authContext.AccessToken = ReadFirstNonEmptyHeader(context.Request, AccessTokenHeader);

        var identity = identityProvider.Resolve(context, out _);
        if (identity is null)
        {
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

                // Audit apenas no INSERT real — re-upserts (LastSeenAt bump) silenciam.
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
            userAccessor.Current = user;

        await _next(context);
    }

    /// <summary>
    /// Invalida a entrada de cache pra um par (tenant, externalUserId).
    /// Admin chama isso após mudar IsAdmin/DisplayName via UI pra que a
    /// próxima request enxergue o estado novo sem esperar o TTL.
    /// </summary>
    public static void InvalidateCache(IMemoryCache cache, string tenantId, string externalUserId)
        => cache.Remove(BuildCacheKey(tenantId, externalUserId));

    private static string BuildCacheKey(string tenantId, string externalUserId)
        => $"user-provisioning:{tenantId}:{externalUserId}";

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
