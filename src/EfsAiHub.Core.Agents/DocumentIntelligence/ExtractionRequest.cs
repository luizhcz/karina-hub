using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Input JSON do executor document_intelligence.
/// outputFormat: "markdown" (default) | "text". Markdown é melhor para LLM
/// consumir estrutura (tabelas, headers). Cache Redis inclui format no key.
/// </summary>
public record ExtractionRequest(
    [property: JsonPropertyName("source")] DocumentSource Source,
    [property: JsonPropertyName("model")] string Model = "prebuilt-layout",
    [property: JsonPropertyName("features")] string[]? Features = null,
    [property: JsonPropertyName("cacheEnabled")] bool CacheEnabled = true,
    [property: JsonPropertyName("outputFormat")] string OutputFormat = "markdown");
