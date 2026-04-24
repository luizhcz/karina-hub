namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Modo de execução de um workflow/agente.
/// Propagado via ExecutionContext (AsyncLocal) para que cada camada
/// ajuste seu comportamento sem um filter centralizado.
/// </summary>
public enum ExecutionMode
{
    /// <summary>Execução normal com persistência, billing e métricas de produção.</summary>
    Production,

    /// <summary>
    /// Execução sandbox/playground: LLM real, tools mockadas (via ToolMocker),
    /// sem persistência de ChatMessage, métricas tagueadas mode=sandbox,
    /// TokenUsage persiste com flag IsSandbox=true (para custo estimado).
    /// </summary>
    Sandbox
}
