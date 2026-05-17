using System.Text;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Core.Agents.Composition;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Implementação default do <see cref="ISystemMessageBuilder"/>.
///
/// Ordem intencional (crítica para prompt caching):
///   1. <c>agentInstructions</c>  — INVARIANTE, compõe o prefixo cacheável
///   2. <c>personaPrompt.SystemSection</c> — volátil por usuário
///
/// OpenAI cacheia prefixo exato até o primeiro token divergente. Colocar
/// persona após instructions garante que usuários diferentes reaproveitem
/// o cache do prompt base (desconto de ~90% em cache hit pra gpt-5.x).
/// </summary>
public sealed class SystemMessageBuilder : ISystemMessageBuilder
{
    public string Build(string agentInstructions, ComposedPersonaPrompt personaPrompt)
    {
        var instructionsLen = agentInstructions?.Length ?? 0;
        if (string.IsNullOrEmpty(personaPrompt.SystemSection))
        {
            // Sem persona: o system message inteiro veio de agent.instructions.
            PromptCompositionAmbient.Track(
                source: "agent.instructions",
                contributorType: nameof(SystemMessageBuilder),
                charOffset: 0,
                charLength: instructionsLen);
            return agentInstructions ?? string.Empty;
        }

        var sb = new StringBuilder(instructionsLen + personaPrompt.SystemSection.Length + 4);
        sb.Append(agentInstructions ?? string.Empty);
        if (!agentInstructions?.EndsWith('\n') ?? true)
            sb.AppendLine();
        sb.AppendLine();
        var personaOffset = sb.Length;
        sb.Append(personaPrompt.SystemSection);

        PromptCompositionAmbient.Track(
            source: "agent.instructions",
            contributorType: nameof(SystemMessageBuilder),
            charOffset: 0,
            charLength: instructionsLen);
        PromptCompositionAmbient.Track(
            source: "persona.system",
            contributorType: nameof(SystemMessageBuilder),
            charOffset: personaOffset,
            charLength: personaPrompt.SystemSection.Length,
            note: "persona composer");

        return sb.ToString();
    }
}
