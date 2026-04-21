namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Fase 7 — Regra de roteamento declarativa. O <c>IEscalationRouter</c> escolhe
/// a regra de maior <see cref="Priority"/> cujo <see cref="Match"/> casa com o
/// <c>AgentEscalationSignal</c> emitido.
/// </summary>
public class RoutingRule
{
    /// <summary>
    /// Expressão de match. Formatos aceitos:
    ///   "category:billing"        → casa por categoria exata
    ///   "tag:priority"            → casa se SuggestedTargetTags contém a tag
    ///   "regex:^refund.*"         → regex sobre Reason
    ///   "any"                     → fallback/default
    /// </summary>
    public required string Match { get; init; }

    /// <summary>ID do nó (agente ou executor) para o qual rotear.</summary>
    public required string TargetNodeId { get; init; }

    /// <summary>Maior valor vence em caso de múltiplos matches. Default: 0.</summary>
    public int Priority { get; init; } = 0;
}
