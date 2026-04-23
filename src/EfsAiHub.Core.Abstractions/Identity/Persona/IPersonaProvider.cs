namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Resolve <see cref="UserPersona"/> a partir de UserId+UserType.
///
/// Contrato: NUNCA lança exceção. Em erro de transporte/timeout/404/5xx,
/// retornar o Anonymous apropriado via <see cref="UserPersonaFactory.Anonymous"/>.
/// Fallback silencioso é responsabilidade do próprio provider HTTP — decorators
/// de cache não precisam saber de política de recovery.
/// </summary>
public interface IPersonaProvider
{
    Task<UserPersona> ResolveAsync(string userId, string userType, CancellationToken ct = default);
}
