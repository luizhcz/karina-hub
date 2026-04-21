namespace EfsAiHub.Core.Orchestration.Enums;

public enum OrchestrationMode
{
    Sequential,
    Concurrent,
    Handoff,
    GroupChat,

    /// <summary>
    /// Grafo dirigido de baixo nível usando WorkflowBuilder.
    /// Suporta todos os tipos de arestas (Direct, Conditional, Switch, FanOut, FanIn)
    /// e permite misturar AIAgents com code executors (DelegateExecutor).
    /// </summary>
    Graph
}
