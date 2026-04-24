namespace EfsAiHub.Core.Agents.Signals;

/// <summary>
/// Sinal emitido por um agente quando precisa escalar/delegar.
/// O agente NÃO conhece peers; apenas descreve categoria e tags desejadas.
/// O <c>IEscalationRouter</c> decide o nó de destino a partir das
/// <c>RoutingRules</c> do workflow.
/// </summary>
public sealed class AgentEscalationSignal
{
    public required string Reason { get; init; }
    public required string Category { get; init; }
    public IReadOnlyList<string> SuggestedTargetTags { get; init; } = [];
    public double Confidence { get; init; } = 1.0;
    public string? Payload { get; init; }
}
