namespace EfsAiHub.Core.Orchestration.Enums;

/// <summary>
/// Tipos de arestas suportados no modo Graph (WorkflowBuilder de baixo nível).
/// </summary>
public enum WorkflowEdgeType
{
    /// <summary>Aresta direta 1→1 sem condição. WorkflowBuilder.AddEdge(source, target)</summary>
    Direct,

    /// <summary>
    /// Aresta condicional 1→1. WorkflowBuilder.AddEdge&lt;string&gt;(source, target, s => s.Contains(condition))
    /// O campo Condition é verificado como substring no output do executor de origem.
    /// </summary>
    Conditional,

    /// <summary>
    /// Ramificação switch 1→N com casos e default.
    /// WorkflowBuilder.AddSwitch(source, b => b.AddCase(...).WithDefault(...))
    /// Define os casos em Cases; o primeiro case cujo Condition for substring do output vence.
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
