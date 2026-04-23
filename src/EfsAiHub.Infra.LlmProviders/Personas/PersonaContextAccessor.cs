using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Infra.LlmProviders.Personas;

/// <summary>
/// Implementação AsyncLocal do <see cref="IPersonaContextAccessor"/>. Mesmo
/// padrão de <c>ProjectContextAccessor</c>. Registrado scoped em DI e
/// populado pelo <c>PersonaResolutionMiddleware</c>.
/// </summary>
public sealed class PersonaContextAccessor : IPersonaContextAccessor
{
    private static readonly AsyncLocal<Holder?> _holder = new();

    public UserPersona? Current
    {
        get => _holder.Value?.Persona;
        set
        {
            var current = _holder.Value;
            if (current is not null) current.Persona = null; // libera reference antiga

            _holder.Value = value is null ? null : new Holder { Persona = value };
        }
    }

    // Holder mutável permite clear em cascata entre escopos Async sem vazar
    // referência para escopos paralelos (pattern do IHttpContextAccessor).
    private sealed class Holder
    {
        public UserPersona? Persona;
    }
}
