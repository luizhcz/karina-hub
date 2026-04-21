using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Input JSON do executor document_intelligence.
/// </summary>
public record ExtractionRequest(
    [property: JsonPropertyName("source")] DocumentSource Source,
    [property: JsonPropertyName("model")] string Model = "prebuilt-layout",
    [property: JsonPropertyName("features")] string[]? Features = null,
    [property: JsonPropertyName("cacheEnabled")] bool CacheEnabled = true);
