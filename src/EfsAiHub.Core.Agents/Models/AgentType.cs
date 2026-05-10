using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Tipo formal do agente. Diferencia comportamento esperado e habilita
/// validações por tipo (ex: Router exige outputSchema com property
/// <c>intent</c> + enum). Custom é o default — agente livre, sem
/// template aplicado, sem validações específicas.
///
/// Persiste como string no jsonb da row (<c>JsonStringEnumConverter</c>
/// aplicado por attribute, conversão restrita a este enum). Membros
/// adicionais (ToolRunner, Conversational) entram conforme cada tipo
/// for refinado.
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
}
