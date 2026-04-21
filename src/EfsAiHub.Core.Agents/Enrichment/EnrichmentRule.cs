using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Enrichment;

/// <summary>
/// Regra declarativa de enrichment aplicada pelo GenericEnricher executor.
/// Armazenada em <see cref="EfsAiHub.Core.Orchestration.Workflows.WorkflowConfiguration.EnrichmentRules"/>.
/// </summary>
public class EnrichmentRule
{
    /// <summary>Condição de match (por response_type).</summary>
    [JsonPropertyName("when")]
    public EnrichmentCondition When { get; init; } = new();

    /// <summary>
    /// Chave do disclaimer a anexar à message (resolvido via <see cref="DisclaimerRegistry"/>).
    /// Null = nenhum disclaimer.
    /// </summary>
    [JsonPropertyName("appendDisclaimer")]
    public string? AppendDisclaimer { get; init; }

    /// <summary>
    /// Defaults a preencher no payload. Chave = campo, valor = fonte.
    /// Fontes suportadas: "from_context" (extrai do bloco [CONTEXT]).
    /// </summary>
    [JsonPropertyName("defaults")]
    public Dictionary<string, string>? Defaults { get; init; }
}

/// <summary>
/// Condição de match para uma <see cref="EnrichmentRule"/>.
/// </summary>
public class EnrichmentCondition
{
    /// <summary>Match por response_type (case-insensitive). Null = match all.</summary>
    [JsonPropertyName("responseType")]
    public string? ResponseType { get; init; }
}
