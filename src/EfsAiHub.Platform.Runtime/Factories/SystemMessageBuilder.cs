using System.Text;
using EfsAiHub.Core.Abstractions.Identity.Persona;

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
        if (string.IsNullOrEmpty(personaPrompt.SystemSection))
            return agentInstructions ?? string.Empty;

        var sb = new StringBuilder((agentInstructions?.Length ?? 0) + personaPrompt.SystemSection.Length + 4);
        sb.Append(agentInstructions ?? string.Empty);
        if (!agentInstructions?.EndsWith('\n') ?? true)
            sb.AppendLine();
        sb.AppendLine();
        sb.Append(personaPrompt.SystemSection);
        return sb.ToString();
    }
}
