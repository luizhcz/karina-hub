namespace EfsAiHub.Core.Orchestration.Enums;

/// <summary>
/// Tipos de arestas suportados no modo Graph (WorkflowBuilder de baixo nível).
/// </summary>
public enum WorkflowEdgeType
{
    /// <summary>Aresta direta 1→1 sem condição. WorkflowBuilder.AddEdge(source, target)</summary>
    Direct,

    /// <summary>
    /// Aresta condicional 1→1 com predicate tipado sobre o output JSON do nó produtor.
    /// Usa <see cref="EfsAiHub.Core.Orchestration.Workflows.EdgePredicate"/> (Path + Operator + Value).
    /// Origem precisa expor schema (agente json_schema ou executor Register&lt;TIn,TOut&gt;) — sem schema, save é rejeitado.
    /// </summary>
    Conditional,

    /// <summary>
    /// Ramificação switch 1→N com casos e default.
    /// Cada case tem seu próprio <see cref="EfsAiHub.Core.Orchestration.Workflows.EdgePredicate"/>;
    /// avaliados em ordem, primeiro match vence; default usado se nenhum casar.
    /// Origem precisa expor schema, mesma regra do Conditional.
    /// </summary>
    Switch,

    /// <summary>
    /// Fan-out 1→N: mensagem enviada a múltiplos executores em paralelo.
    /// WorkflowBuilder.AddFanOutEdge(source, targets[])
    /// Define os alvos em Targets.
    /// </summary>
    FanOut,

    /// <summary>
    /// Fan-in N→1: barreira que aguarda todos os executores de origem antes de prosseguir.
    /// WorkflowBuilder.AddFanInBarrierEdge(sources[], target)
    /// Define as origens em Sources e o alvo em To.
    /// </summary>
    FanIn
}
