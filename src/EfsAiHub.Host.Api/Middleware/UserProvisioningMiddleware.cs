using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Users;
using EfsAiHub.Host.Api.Services;
using Microsoft.Extensions.Caching.Memory;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Auto-cadastra o usuário no diretório (tabela aihub.users) no primeiro
/// request e popula <see cref="IUserContextAccessor"/> pra downstream
/// (AdminGate, controllers, audit). Cache em memória de 60s por
/// (TenantId, ExternalUserId) evita martelar o DB com upserts redundantes.
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
        IMemoryCache cache,
        ILogger<UserProvisioningMiddleware> logger)
    {
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
                return await directory.UpsertAsync(
                    identity.ExternalUserId,
                    identity.UserType,
                    tenantId,
                    displayName: null,
                    context.RequestAborted);
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
}
