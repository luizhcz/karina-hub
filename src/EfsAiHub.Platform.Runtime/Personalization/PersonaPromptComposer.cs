using System.Text;
using EfsAiHub.Core.Abstractions.Identity.Persona;

namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Implementação default do <see cref="IPersonaPromptComposer"/>. Compõe:
///  - <see cref="ComposedPersonaPrompt.SystemSection"/>: seção Markdown com persona
///    + tone_policy — concatenada ao final do system message do agente.
///  - <see cref="ComposedPersonaPrompt.UserReinforcement"/>: 1 linha curta
///    (≤15 tokens) anexada à user message corrente — combate lost-in-the-middle.
///
/// Pura e sem I/O — fácil de testar isoladamente.
/// Ordem das seções (Persona → Tone Policy) definida para que o cache de prefixo
/// do OpenAI preserve o prompt base invariante acima desta seção.
/// </summary>
public sealed class PersonaPromptComposer : IPersonaPromptComposer
{
    public ComposedPersonaPrompt Compose(UserPersona? persona)
    {
        if (persona is null || persona.IsAnonymous)
            return ComposedPersonaPrompt.Empty;

        var tonePolicy = TonePolicyTable.Lookup(persona.Segment, persona.RiskProfile);

        return new ComposedPersonaPrompt(
            SystemSection: BuildSystemSection(persona, tonePolicy),
            UserReinforcement: BuildUserReinforcement(persona));
    }

    private static string BuildSystemSection(UserPersona persona, string? tonePolicy)
    {
        var sb = new StringBuilder();
        sb.AppendLine(PersonaPromptSections.PersonaHeader);
        AppendField(sb, PersonaPromptSections.FieldSegment, persona.Segment);
        AppendField(sb, PersonaPromptSections.FieldRiskProfile, persona.RiskProfile);
        AppendField(sb, PersonaPromptSections.FieldDisplayName, persona.DisplayName);
        AppendField(sb, PersonaPromptSections.FieldAdvisor, persona.AdvisorId);

        if (!string.IsNullOrWhiteSpace(tonePolicy))
        {
            sb.AppendLine();
            sb.AppendLine(PersonaPromptSections.TonePolicyHeader);
            sb.Append(tonePolicy);
        }

        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("- ").Append(label).Append(": ").AppendLine(value);
    }

    // Reinforcement minimalista. Apenas 2 campos (segment+risk) — suficiente pra
    // ancorar last-token bias sem inflar cada turn com >100 tokens redundantes.
    private static string? BuildUserReinforcement(UserPersona persona)
    {
        if (string.IsNullOrWhiteSpace(persona.Segment) && string.IsNullOrWhiteSpace(persona.RiskProfile))
            return null;

        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(persona.Segment))
            parts.Add($"persona.segment={persona.Segment}");
        if (!string.IsNullOrWhiteSpace(persona.RiskProfile))
            parts.Add($"persona.risk={persona.RiskProfile}");

        return $"[{string.Join(", ", parts)}]";
    }
}
