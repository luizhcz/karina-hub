using System.Text.Json;

namespace EfsAiHub.Core.Agents.RouterIntents;

/// <summary>
/// Defaults canônicos do agente <c>Router</c>. Aplicados pelo
/// <c>AgentTemplateService</c> quando admin não cadastra schema próprio.
/// Concentrados aqui pra evitar drift entre seed, runtime e teste — um único
/// ponto altera o canônico.
///
/// Shape do output: <c>{ intent, confidence, reason, operationalMemory: { last_intent, last_reason } }</c>.
/// O enum de <c>intent</c> é sobrescrito em runtime pelo <c>ChatOptionsBuilder</c>
/// com os nomes do pool de intents do agent.
/// </summary>
public static class RouterDefaults
{
    public const string SchemaName = "RouterOutput";
    public const string SchemaDescription = "Output canônico do Router (intent + confidence + reason + operationalMemory)";

    /// <summary>
    /// Schema JSON do StructuredOutput. O enum em <c>intent</c> é placeholder —
    /// o ChatOptionsBuilder substitui pelos nomes resolvidos do pool antes da
    /// chamada ao LLM. Mantemos um valor sentinel pra que o schema fique
    /// válido em memória antes do enriquecimento.
    /// </summary>
    public const string OutputSchemaJson = """
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "intent": {
          "type": "string",
          "description": "Categoria escolhida do enum de intents resolvido em runtime."
        },
        "confidence": {
          "type": "number",
          "minimum": 0,
          "maximum": 1,
          "description": "Confiança da classificação (0..1)."
        },
        "reason": {
          "type": "string",
          "description": "Justificativa curta da escolha (uso interno de auditoria/debug)."
        },
        "operationalMemory": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "last_intent": { "type": "string" },
            "last_reason": { "type": "string" }
          },
          "required": ["last_intent", "last_reason"]
        }
      },
      "required": ["intent", "confidence", "reason", "operationalMemory"]
    }
    """;

    /// <summary>
    /// Schema do payload de memória persistido em <c>aihub.operational_memory</c>.
    /// Espelha o sub-objeto <c>operationalMemory</c> do output — o middleware
    /// extrai esse campo do JSON top-level emitido pelo LLM e persiste.
    /// </summary>
    public const string OperationalMemorySchemaJson = """
    {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "last_intent": {
          "type": "string",
          "description": "Nome da última intent classificada pelo Router."
        },
        "last_reason": {
          "type": "string",
          "description": "Razão curta (≤200 chars) da última classificação."
        }
      },
      "required": ["last_intent", "last_reason"]
    }
    """;

    public const int OperationalMemoryMaxBytes = 2048;

    public static AgentStructuredOutputDefinition OutputSchema() => new()
    {
        ResponseFormat = "json_schema",
        SchemaName = SchemaName,
        SchemaDescription = SchemaDescription,
        Schema = JsonDocument.Parse(OutputSchemaJson),
    };

    public static AgentOperationalMemoryDefinition OperationalMemoryV1() => new()
    {
        Schema = JsonDocument.Parse(OperationalMemorySchemaJson),
        MaxBytes = OperationalMemoryMaxBytes,
    };

    /// <summary>
    /// Janela de histórico padrão consumida pelo Router. Manter curto reduz
    /// risco de o classificador se confundir com turnos antigos. Valor é
    /// hardcoded no V1 (não exposto no wizard).
    /// </summary>
    public const int HistoryMessages = 5;
}
