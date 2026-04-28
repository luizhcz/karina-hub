namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluationResultRepository
{
    Task<IReadOnlyList<EvaluationResult>> ListByRunAsync(
        string runId,
        bool? passedFilter = null,
        string? evaluatorNameFilter = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default);

    Task<int> CountByRunAsync(string runId, bool? passedFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Insere um batch de resultados e atualiza <see cref="EvaluationRunProgress"/>
    /// (rolling counters via INSERT ON CONFLICT DO UPDATE col = col + EXCLUDED.col)
    /// na mesma transação.
    /// </summary>
    Task AppendBatchAsync(
        string runId,
        IReadOnlyList<EvaluationResult> results,
        CancellationToken ct = default);

    Task<EvaluationRunProgress?> GetProgressAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Agrega tokens e custo a partir de llm_token_usage (single source of truth).
    /// Cobre chamadas do agente sob teste e dos judges MEAI/Foundry sem duplicação.
    /// JOIN com model_pricing usando o pricing vigente em llm_token_usage.CreatedAt.
    /// Retorna zeros se nada foi capturado.
    /// </summary>
    Task<EvaluationRunUsage> GetUsageAsync(string runId, CancellationToken ct = default);
}

public sealed record EvaluationRunUsage(
    long InputTokens,
    long OutputTokens,
    long TotalTokens,
    decimal TotalCostUsd);
