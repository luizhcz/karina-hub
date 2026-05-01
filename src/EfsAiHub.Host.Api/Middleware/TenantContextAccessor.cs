using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Implementação singleton do <see cref="ITenantContextAccessor"/> usando AsyncLocal —
/// segue o mesmo pattern de <see cref="ProjectContextAccessor"/>. Permite que o tenant
/// definido pelo middleware HTTP flua para escopos internos (IDbContextFactory rodando
/// no DatabaseBootstrap, background tasks etc.) sem cair em "Cannot resolve scoped service
/// from root provider".
/// </summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContext?> _ambient = new();

    public TenantContext Current
    {
        get => _ambient.Value ?? TenantContext.Default;
        set => _ambient.Value = value;
    }
}
