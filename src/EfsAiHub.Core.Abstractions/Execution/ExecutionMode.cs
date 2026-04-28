namespace EfsAiHub.Core.Abstractions.Execution;

/// <summary>
/// Modo de execução de um workflow/agente. Propagado via ExecutionContext (AsyncLocal).
/// </summary>
public enum ExecutionMode
{
    Production,

    /// <summary>
    /// Sandbox/playground: LLM real, tools mockadas, sem persistência de ChatMessage.
    /// TokenUsage persiste com IsSandbox=true para custo estimado.
    /// </summary>
    Sandbox,

    /// <summary>
    /// Eval run: LLM real, tools reais (ToolCalledCheck depende de tools executando),
    /// sem persistência de ChatMessage. LlmTokenUsage persiste com
    /// Metadata.source='evaluation' e ExecutionId='eval:{RunId}'. Custo conta no budget cap.
    /// </summary>
    Evaluation
}
