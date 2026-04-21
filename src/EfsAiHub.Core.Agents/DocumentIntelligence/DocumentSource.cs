using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.DocumentIntelligence;

/// <summary>
/// Fonte do PDF para extração: URL com SAS token (preferido) ou bytes em base64.
/// </summary>
public record DocumentSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("bytes")] string? Bytes);
