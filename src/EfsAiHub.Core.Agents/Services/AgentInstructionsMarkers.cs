namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Marcadores estáveis usados pelo <c>AgentDefinitionComposer</c> ao envolver
/// blocos auto-gerados dentro de <see cref="AgentDefinition.Instructions"/> e
/// pelo <c>AgentDefinitionDecomposer</c> ao reverter o agente pra forma
/// editável no GET. Cada bloco tem um par <c>Begin</c>/<c>End</c> com o mesmo
/// rótulo; o conteúdo entre eles é considerado derivado de outra fonte
/// (intents do pool global, skills referenciadas, escopo do worker) e nunca
/// vem do texto que o owner do agente digita.
///
/// O formato HTML comment garante que o LLM enxergue como comentário (ignora
/// na maioria dos modelos chat) e mantém os marcadores fora de qualquer parse
/// markdown intermediário. Strings literais idênticas no texto autoral são
/// escapadas pelo composer antes da injeção pra preservar a invariante
/// <c>Decompose(Compose(x)) ≡ x</c>.
/// </summary>
public static class AgentInstructionsMarkers
{
    public const string IntentsBegin = "<!-- aihub:auto-intents -->";
    public const string IntentsEnd = "<!-- aihub:auto-intents-end -->";

    public const string SkillsBegin = "<!-- aihub:auto-skills -->";
    public const string SkillsEnd = "<!-- aihub:auto-skills-end -->";

    public const string WorkerScopeBegin = "<!-- aihub:auto-worker-scope -->";
    public const string WorkerScopeEnd = "<!-- aihub:auto-worker-scope-end -->";
}
