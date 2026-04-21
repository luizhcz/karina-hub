namespace EfsAiHub.Core.Abstractions.Identity;

/// <summary>
/// Abstração de resolução de usuário que mantém o Core isolado de HttpContext.
/// Implementação concreta reside em Host.Api/Middleware (Fase 6/9).
/// </summary>
public interface IUserContextResolver
{
    UserContext? TryResolve(out string? errorMessage);
}
