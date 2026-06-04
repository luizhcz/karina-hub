using System.Text.Json;

namespace EfsAiHub.Core.Agents.RouterIntents;

/// <summary>
/// Defaults canônicos do agente <c>Router</c>. Aplicados pelo
/// <c>AgentTemplateService</c> quando admin não cadastra schema próprio.
/// Concentrados aqui pra evitar drift entre seed, runtime e teste — um único
/// ponto altera o canônico.
///
/// Shape do output:
/// <c>{ intent, confidence, reason, candidate_intents, operationalMemory:
/// { last_intent, last_reason, clarification_depth } }</c>.
/// O enum de <c>intent</c> é sobrescrito em runtime pelo
/// <c>OutputSchemaRenderer</c> com os nomes do pool de intents do agent.
///
/// <para>
/// <c>candidate_intents</c> só é preenchido quando <c>intent =
/// "needs_clarification"</c> (mensagem ambígua entre ≥2 intents de negócio
/// com confidence similar). Vazio nos demais casos. <c>clarification_depth</c>
/// é loop guard: incrementado a cada turno consecutivo com
/// <c>needs_clarification</c>; em <c>≥ 2</c> o Router deve cair em
/// <c>out_of_scope</c> com reason explicando a desistência.
/// </para>
/// </summary>
public static class RouterDefaults
{
    public const string SchemaName = "RouterOutput";
    public const string SchemaDescription =
        "Output canônico do Router (intent + confidence + reason + candidate_intents + operationalMemory)";

    /// <summary>
    /// Schema JSON do StructuredOutput. O enum em <c>intent</c> é placeholder —
    /// o <c>OutputSchemaRenderer</c> substitui pelos nomes resolvidos do pool
    /// antes da chamada ao LLM. Mantemos um valor sentinel pra que o schema
    /// fique válido em memória antes do enriquecimento.
    ///
    /// <para>
    /// <c>candidate_intents</c> é <c>required</c> com default array vazio —
    /// LLM emite <c>[]</c> quando <c>intent != "needs_clarification"</c>.
    /// Mantém o schema fechado (OpenAI strict mode) sem ramificar o contrato.
    /// </para>
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
        "candidate_intents": {
          "type": "array",
          "description": "Intents candidatas quando intent='needs_clarification' (mín. 2 itens). Array vazio nos demais casos.",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "intent": { "type": "string" },
              "confidence": { "type": "number", "minimum": 0, "maximum": 1 }
            },
            "required": ["intent", "confidence"]
          }
        },
        "operationalMemory": {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "last_intent": { "type": "string" },
            "last_reason": { "type": "string" },
            "clarification_depth": {
              "type": "integer",
              "minimum": 0,
              "description": "Turnos consecutivos com needs_clarification. 0 quando intent != needs_clarification. Em >=2 o Router cai em out_of_scope."
            },
            "last_candidate_intents": {
              "type": "array",
              "description": "Snapshot das candidate_intents do último turno needs_clarification (só nomes). Vazio nos demais casos.",
              "items": { "type": "string" }
            }
          },
          "required": ["last_intent", "last_reason", "clarification_depth", "last_candidate_intents"]
        }
      },
      "required": ["intent", "confidence", "reason", "candidate_intents", "operationalMemory"]
    }
    """;

    /// <summary>
    /// Schema do payload de memória persistido em <c>aihub.operational_memory</c>.
    /// Espelha o sub-objeto <c>operationalMemory</c> do output — o middleware
    /// extrai esse campo do JSON top-level emitido pelo LLM e persiste.
    ///
    /// <para>
    /// <c>last_candidate_intents</c>: nomes (sem confidence) das intents
    /// candidatas do último turno onde o Router emitiu
    /// <c>needs_clarification</c>. Vazio nos demais casos. Essencial pro
    /// turno seguinte: quando o usuário responde à pergunta de
    /// desambiguação ("Renda fixa ou variável?" → "fixa"), o Router precisa
    /// SABER quais eram as candidatas pra mapear a resposta — sem isso, fica
    /// dependente de inferir do texto do Clarifier no histórico do chat, o
    /// que é frágil.
    /// </para>
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
        },
        "clarification_depth": {
          "type": "integer",
          "minimum": 0,
          "description": "Turnos consecutivos com intent='needs_clarification'. Resetado a 0 quando o Router escolhe qualquer outra intent."
        },
        "last_candidate_intents": {
          "type": "array",
          "description": "Nomes das candidate_intents do último turno needs_clarification. Vazio quando intent anterior != needs_clarification. Permite ao Router resolver clarification follow-up no turno seguinte.",
          "items": { "type": "string" }
        }
      },
      "required": ["last_intent", "last_reason", "clarification_depth", "last_candidate_intents"]
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
