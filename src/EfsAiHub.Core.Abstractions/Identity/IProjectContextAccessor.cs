namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Acessor scoped do <see cref="ProjectContext"/> resolvido por requisição.
/// Implementação concreta vive em Host.Api e é populada pelo ProjectMiddleware.
/// Componentes downstream (DbContext, throttlers, guards) consomem este acessor
/// em vez de depender de HttpContext.
/// </summary>
public interface IProjectContextAccessor
{
    ProjectContext Current { get; set; }
}
