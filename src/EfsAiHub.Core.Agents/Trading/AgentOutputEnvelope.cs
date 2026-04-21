using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents.Trading;

/// <summary>
/// Envelope genérico para output de agentes no protocolo AG-UI.
/// Segue o pattern "discriminated union": campos fixos no envelope,
/// payload tipado por agente via structured output.
///
/// Cada agente define seu próprio JSON Schema incluindo o envelope + payload tipado.
/// O frontend usa <c>ResponseType</c> como discriminante para selecionar o renderer.
/// </summary>
public class AgentOutputEnvelope
{
    /// <summary>Discriminante: identifica o tipo de resposta e seleciona o renderer no frontend.</summary>
    [JsonPropertyName("response_type")]
    public required string ResponseType { get; set; }

    /// <summary>Mensagem textual para o usuário.</summary>
    [JsonPropertyName("message")]
    public required string Message { get; set; }

    /// <summary>Componente de UI a renderizar no frontend.</summary>
    [JsonPropertyName("ui_component")]
    public required string UiComponent { get; set; }

    /// <summary>
    /// Payload específico do agente. Schema livre — cada agente define sua estrutura
    /// no StructuredOutput. Mantido como JsonElement para deserialização lazy.
    /// </summary>
    [JsonPropertyName("payload")]
    public System.Text.Json.JsonElement? Payload { get; set; }

    /// <summary>
    /// Metadata de enrichment aplicado pelo GenericEnricher (observabilidade).
    /// Null se nenhum enrichment foi aplicado.
    /// </summary>
    [JsonPropertyName("enrichment")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EnrichmentMetadata? Enrichment { get; set; }
}

/// <summary>
/// Metadata de enrichment determinístico aplicado pelo GenericEnricher.
/// Visível no SharedStatePanel do frontend para observabilidade.
/// </summary>
public class EnrichmentMetadata
{
    [JsonPropertyName("disclaimers_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DisclaimersApplied { get; set; }

    [JsonPropertyName("defaults_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DefaultsApplied { get; set; }

    [JsonPropertyName("validated")]
    public bool Validated { get; set; }

    [JsonPropertyName("enriched_at")]
    public DateTime EnrichedAt { get; set; }
}
