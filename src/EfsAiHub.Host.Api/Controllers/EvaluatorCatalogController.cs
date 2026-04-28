using EfsAiHub.Host.Api.Models.Responses.Evaluation;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EfsAiHub.Host.Api.Controllers;

/// <summary>
/// Catálogo de evaluators disponíveis. Static — não depende de DB.
/// Agrupado por dimensão com sub-label de fonte (Local|MEAI|Foundry) para
/// evitar colisão quando MEAI e Foundry expõem evaluators com mesmo nome.
/// </summary>
[ApiController]
[Route("api/evaluator-config")]
[Produces("application/json")]
public sealed class EvaluatorCatalogController : ControllerBase
{
    [HttpGet("catalog")]
    [SwaggerOperation(Summary = "Lista evaluators disponíveis (Foundry/Local/MEAI)")]
    [ProducesResponseType(typeof(IReadOnlyList<EvaluatorCatalogEntry>), StatusCodes.Status200OK)]
    public IActionResult GetCatalog() => Ok(BuildCatalog());

    private static IReadOnlyList<EvaluatorCatalogEntry> BuildCatalog() => new EvaluatorCatalogEntry[]
    {
        new("Local", "KeywordCheck", "Heuristic",
            "Verifica presença de keywords no output. Suporta matchMode any|all, caseSensitive.",
            RequiresParams: true,
            ParamsExampleJson: """{"keywords":["weather","temperature"],"matchMode":"any","caseSensitive":false}"""),
        new("Local", "ToolCalledCheck", "Heuristic",
            "Verifica que o agente invocou tool específica. Usa ExpectedToolCalls do test case se params.expectedToolName ausente.",
            RequiresParams: false,
            ParamsExampleJson: """{"expectedToolName":"get_weather"}"""),
        new("Local", "ContainsExpected", "Heuristic",
            "Substring match contra ExpectedOutput do test case.",
            RequiresParams: false,
            ParamsExampleJson: """{"caseSensitive":false}"""),

        new("Meai", "Relevance", "Relevance",
            "Quão relevante é a resposta para a query. LLM-as-judge. Score 1..5 normalizado para 0..1.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Coherence", "Coherence",
            "Coerência interna do texto. LLM-as-judge.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Groundedness", "Groundedness",
            "Quanto a resposta está fundamentada no contexto fornecido (RAG). Exige contexto/passages no test case.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "ToolCallAccuracy", "ToolUse",
            "LLM-as-judge avalia se a tool call do agente é apropriada para a query.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Fluency", "Fluency",
            "Fluência gramatical e legibilidade da resposta. LLM-as-judge.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Completeness", "Completeness",
            "Quão completa é a resposta vs ExpectedOutput. LLM-as-judge.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Equivalence", "Equivalence",
            "Equivalência semântica entre output e ExpectedOutput. LLM-as-judge.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "TaskAdherence", "TaskAdherence",
            "Quão fielmente o output segue a task definida nas instructions. LLM-as-judge. [Preview]",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "IntentResolution", "Intent",
            "Resolução do intent declarado vs o real. LLM-as-judge. [Preview]",
            RequiresParams: false, ParamsExampleJson: null),
        new("Meai", "Retrieval", "Retrieval",
            "Avalia qualidade do retrieval em pipelines RAG. LLM-as-judge.",
            RequiresParams: false, ParamsExampleJson: null),

        // Foundry Safety chama Azure AI Foundry Content Safety API (não chat
        // completion). Exige projects.settings.evaluation.foundry.projectEndpoint
        // + DefaultAzureCredential. Sem projectEndpoint, EvaluatorFactory pula
        // esses bindings com warning.
        new("Foundry", "Violence", "Safety",
            "Detecta conteúdo violento. Exige projects.settings.evaluation.foundry.projectEndpoint configurado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "Sexual", "Safety",
            "Detecta conteúdo sexual. Exige projects.settings.evaluation.foundry.projectEndpoint configurado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "SelfHarm", "Safety",
            "Detecta conteúdo de auto-mutilação. Exige projects.settings.evaluation.foundry.projectEndpoint configurado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "HateAndUnfairness", "Safety",
            "Detecta discurso de ódio e injustiça (raça, gênero, etc). Exige projects.settings.evaluation.foundry.projectEndpoint configurado.",
            RequiresParams: false, ParamsExampleJson: null),
        // Foundry Quality reusa as mesmas métricas MEAI mas com judge dedicado.
        new("Foundry", "Relevance", "Relevance",
            "MEAI Relevance com judge Foundry-deployment dedicado. Use quando Privacy/Compliance exige isolamento de tenant.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "Coherence", "Coherence",
            "MEAI Coherence com judge Foundry-deployment dedicado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "Groundedness", "Groundedness",
            "MEAI Groundedness com judge Foundry-deployment dedicado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "ToolCallAccuracy", "ToolUse",
            "MEAI ToolCallAccuracy com judge Foundry-deployment dedicado.",
            RequiresParams: false, ParamsExampleJson: null),
        new("Foundry", "TaskAdherence", "TaskAdherence",
            "MEAI TaskAdherence com judge Foundry-deployment dedicado.",
            RequiresParams: false, ParamsExampleJson: null),
    };
}
