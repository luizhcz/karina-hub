namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Acessor scoped da persona resolvida na requisição corrente. Populado por
/// <c>PersonaResolutionMiddleware</c> após <c>UserIdentityResolver</c>.
/// Null quando não há identidade ou quando a resolução falhou silenciosamente.
///
/// Mesmo pattern de <see cref="IProjectContextAccessor"/>.
/// </summary>
public interface IPersonaContextAccessor
{
    UserPersona? Current { get; set; }
}
