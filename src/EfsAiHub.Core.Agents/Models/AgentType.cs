using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Tipo formal do agente. Diferencia comportamento esperado e habilita
/// validações por tipo (ex: Router exige outputSchema com property
/// <c>intent</c> + enum). Custom é o default — agente livre, sem
/// template aplicado, sem validações específicas.
///
/// Persiste como string no jsonb da row (<c>JsonStringEnumConverter</c>
/// aplicado por attribute, conversão restrita a este enum).
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

    /// <summary>
    /// Chat / assistant. Multi-turn com histórico persistente, interage com
    /// humano em tempo real e mantém contexto entre turns. Sempre roda em
    /// workflow com <c>Configuration.InputMode=Chat</c>. Output estruturado
    /// é obrigatório com shape canônico:
    /// <c>{ ui_component: enum, message: string, output: &lt;subschema livre&gt; }</c>.
    /// O frontend renderer consome <c>ui_component</c> pra escolher
    /// componente UI; <c>message</c> é texto humano; <c>output</c> é payload
    /// customizável pelo agente (subschema declarado pelo user). A lista de
    /// valores válidos de <c>ui_component</c> vive em
    /// <c>metadata['x-conversational-ui-components']</c> como JSON array
    /// e é injetada como enum no schema pelo codec ao montar o payload.
    /// Defaults: modelo balanced, Temperature 0.5–0.8, MaxTokens 1500–2000,
    /// SecurityGuardrails recomendado.
    /// </summary>
    Conversational = 4,
}
