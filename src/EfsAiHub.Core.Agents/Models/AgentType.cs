using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Tipo formal do agente. Diferencia comportamento esperado e habilita
/// validações por tipo (ex: Router exige outputSchema com property
/// <c>intent</c> + enum). Custom é o default — agente livre, sem
/// template aplicado, sem validações específicas.
///
/// Persiste como string no jsonb da row (<c>JsonStringEnumConverter</c>
/// aplicado por attribute, conversão restrita a este enum). Membro
/// adicional (Conversational) entra conforme o tipo for refinado.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentType
{
    Custom = 0,
    Router = 1,

    /// <summary>
    /// Specialist/domain expert. Recebe input estruturado e produz análise
    /// rica/multifator. Defaults: modelo full, MaxTokens 2000–4000,
    /// StructuredOutput recomendado, OperationalMemory off. O "domínio" é
    /// declarado em <c>metadata['x-worker-scope']</c> e injetado em runtime
    /// pelo <c>ChatOptionsBuilder</c> (paralelo ao bloco Router).
    /// </summary>
    Worker = 2,

    /// <summary>
    /// Function-caller / action agent. Decide qual tool chamar com quais
    /// argumentos pra cumprir uma tarefa que exige ação no mundo. LLM como
    /// executor (não como respondedor). Defaults: modelo full, Temperature
    /// 0–0.3 (determinismo), Tools obrigatório (warning se vazio),
    /// AccountGuard + SecurityGuardrails recomendados (defaults on no
    /// wizard). Política de aprovação humana (HITL) declarada em
    /// <c>metadata['x-tool-runner-hitl-required']</c> — flag declarativo
    /// usado pra emitir warnings de consistência no save; runtime não
    /// enforça bloqueio efetivo de invocação.
    /// </summary>
    ToolRunner = 3,
}
