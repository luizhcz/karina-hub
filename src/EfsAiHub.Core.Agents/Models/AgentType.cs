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
/// adicionais (Worker, ToolRunner, Conversational, etc.) entram em
/// futuras revisões conforme cada tipo for refinado.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentType
{
    Custom = 0,
    Router = 1,
}
