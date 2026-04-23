using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Orquestra a resolução de <see cref="UserPersona"/> para uma requisição HTTP:
/// (1) extrai identidade via <see cref="UserIdentityResolver"/>, (2) delega
/// ao <see cref="IPersonaProvider"/> (que nunca lança).
///
/// Separação de responsabilidades: middleware é só glue (~10 linhas, não
/// testável em isolamento). Service é puro e testável 100%.
/// Mesmo pattern usado por <see cref="UserIdentityResolver"/>.
/// </summary>
public sealed class PersonaResolutionService
{
    private readonly UserIdentityResolver _identityResolver;
    private readonly IPersonaProvider _personaProvider;

    public PersonaResolutionService(
        UserIdentityResolver identityResolver,
        IPersonaProvider personaProvider)
    {
        _identityResolver = identityResolver;
        _personaProvider = personaProvider;
    }

    /// <summary>
    /// Resolve a persona dos headers. Retorna null quando não há identidade
    /// válida (caller decide seguir anônimo ou bloquear).
    /// </summary>
    public async Task<UserPersona?> TryResolveAsync(
        IHeaderDictionary headers, CancellationToken ct = default)
    {
        var identity = _identityResolver.TryResolve(headers, out _);
        if (identity is null) return null;

        return await _personaProvider.ResolveAsync(identity.UserId, identity.UserType, ct)
            .ConfigureAwait(false);
    }
}
