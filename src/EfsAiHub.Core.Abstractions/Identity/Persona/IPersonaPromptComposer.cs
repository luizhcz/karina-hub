namespace EfsAiHub.Core.Abstractions.Identity.Persona;

/// <summary>
/// Compõe as duas representações textuais da persona consumidas pelo LLM:
///  - <see cref="ComposedPersonaPrompt.SystemSection"/>: bloco Markdown com
///    persona + tone_policy, concatenado ao final do system message do agente.
///  - <see cref="ComposedPersonaPrompt.UserReinforcement"/>: hint curto
///    (≤15 tokens) anexado ao fim da user message — combate lost-in-the-middle.
///
/// Interface existe para quebrar o acoplamento direto do <c>AgentFactory</c>
/// com a <c>TonePolicyTable</c> estática (DIP + testabilidade).
/// </summary>
public interface IPersonaPromptComposer
{
    ComposedPersonaPrompt Compose(UserPersona? persona);
}

public sealed record ComposedPersonaPrompt(string? SystemSection, string? UserReinforcement)
{
    public static readonly ComposedPersonaPrompt Empty = new(null, null);
    public bool HasAnyContent => SystemSection is not null || UserReinforcement is not null;
}
