using EfsAiHub.Core.Abstractions.Identity;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Implementação scoped do <see cref="ITenantContextAccessor"/>. Default cai em
/// <see cref="TenantContext.Default"/> até o middleware popular o tenant da request.
/// </summary>
public sealed class TenantContextAccessor : ITenantContextAccessor
{
    public TenantContext Current { get; set; } = TenantContext.Default;
}
