namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Compõe as duas representações textuais da persona consumidas pelo LLM:
///  - <see cref="ComposedPersonaPrompt.SystemSection"/>: bloco Markdown com
///    persona + tone_policy, concatenado ao final do system message do agente.
///  - <see cref="ComposedPersonaPrompt.UserReinforcement"/>: hint curto
///    (≤15 tokens) anexado ao fim da user message — combate lost-in-the-middle.
///
/// Template usado vem de <see cref="IPersonaPromptTemplateRepository"/>:
/// cadeia de fallback 5 níveis (ver ADR 003):
///   1. <c>project:{projectId}:agent:{agentId}:{userType}</c>  (mais específico)
///   2. <c>project:{projectId}:{userType}</c>
///   3. <c>agent:{agentId}:{userType}</c>
///   4. <c>global:{userType}</c>
///   5. null (persona fica sem bloco)
/// Interface garante DIP + testabilidade do AgentFactory.
///
/// Assíncrono porque o template é resolvido via cache (L1 sync + L2 Redis
/// async). Método síncrono forçaria sync-over-async no hot path.
/// </summary>
public interface IPersonaPromptComposer
{
    Task<ComposedPersonaPrompt> ComposeAsync(
        UserPersona? persona,
        string? agentId,
        string? projectId = null,
        CancellationToken ct = default);
}

public sealed record ComposedPersonaPrompt(string? SystemSection, string? UserReinforcement)
{
    public static readonly ComposedPersonaPrompt Empty = new(null, null);
    public bool HasAnyContent => SystemSection is not null || UserReinforcement is not null;
}
