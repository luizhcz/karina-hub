namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Acessor scoped do <see cref="TenantContext"/> resolvido por requisição.
/// Implementação concreta vive em Host.Api e é populada pelo TenantMiddleware.
/// Componentes downstream (DbContext, throttlers, guards) consomem este acessor
/// em vez de depender de HttpContext.
/// </summary>
public interface ITenantContextAccessor
{
    TenantContext Current { get; set; }
}
