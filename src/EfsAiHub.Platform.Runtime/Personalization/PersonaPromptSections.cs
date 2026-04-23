namespace EfsAiHub.Platform.Runtime.Personalization;

/// <summary>
/// Constantes de marcação Markdown usadas pelo <see cref="PersonaPromptComposer"/>.
/// Centralizadas aqui pra evitar magic strings espalhadas (divergência entre
/// composer, testes e métricas quando alguém renomeia uma seção).
/// </summary>
internal static class PersonaPromptSections
{
    public const string PersonaHeader = "## Persona do cliente";
    public const string TonePolicyHeader = "## Tone Policy";

    // Campos da seção Persona — manter em sync com o DTO UserPersona + TonePolicyTable.
    public const string FieldSegment = "Segment";
    public const string FieldRiskProfile = "Risk profile";
    public const string FieldDisplayName = "Display name";
    public const string FieldAdvisor = "Advisor";
}
