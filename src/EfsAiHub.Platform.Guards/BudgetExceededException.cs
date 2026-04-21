namespace EfsAiHub.Platform.Guards;

/// <summary>
/// Lançada pelo TokenTrackingChatClient quando o hard cap de tokens LLM por execução
/// (<see cref="EfsAiHub.Core.Orchestration.Workflows.WorkflowConfiguration.MaxTokensPerExecution"/>) é ultrapassado.
/// Classificada como <see cref="EfsAiHub.Core.Orchestration.Enums.ErrorCategory.BudgetExceeded"/>.
/// </summary>
public sealed class BudgetExceededException : Exception
{
    public long TotalTokens { get; }
    public int MaxTokensPerExecution { get; }
    public decimal? TotalCostUsd { get; }
    public decimal? MaxCostUsd { get; }
    public bool IsCostCause => MaxCostUsd is not null;

    public BudgetExceededException(long totalTokens, int maxTokensPerExecution)
        : base($"Limite de tokens LLM por execução excedido ({totalTokens}/{maxTokensPerExecution}).")
    {
        TotalTokens = totalTokens;
        MaxTokensPerExecution = maxTokensPerExecution;
    }

    /// <summary>Fase 2 — ctor para estouro de custo em USD.</summary>
    public BudgetExceededException(decimal totalCostUsd, decimal maxCostUsd, long totalTokens)
        : base($"Limite de custo LLM por execução excedido (US$ {totalCostUsd:F6}/{maxCostUsd:F6}).")
    {
        TotalTokens = totalTokens;
        MaxTokensPerExecution = 0;
        TotalCostUsd = totalCostUsd;
        MaxCostUsd = maxCostUsd;
    }
}
