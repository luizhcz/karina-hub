using EfsAiHub.Core.Agents.Signals;
using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Core.Orchestration.Routing;

/// <summary>
/// Fase 7 — Decide o nó de destino para um <see cref="AgentEscalationSignal"/>
/// avaliando as <see cref="RoutingRule"/>s do workflow. O agente não conhece peers.
/// </summary>
public interface IEscalationRouter
{
    /// <summary>
    /// Retorna o <c>TargetNodeId</c> da regra de maior prioridade cujo <c>Match</c>
    /// casa com o sinal, ou <c>null</c> se nenhuma regra combinar.
    /// </summary>
    string? Route(AgentEscalationSignal signal, IReadOnlyList<RoutingRule> rules);
}
