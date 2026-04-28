namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluationRunRepository
{
    Task<EvaluationRun?> GetByIdAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationRun>> ListByAgentDefinitionAsync(
        string agentDefinitionId,
        int? skip = null,
        int? take = null,
        EvaluationTriggerSource? triggerSourceFilter = null,
        CancellationToken ct = default);

    /// <summary>
    /// Insere uma nova run em <c>Pending</c>. Idempotente por
    /// (AgentVersionId, TriggerSource=AgentVersionPublished) via unique index parcial.
    /// Retorna a run criada/existente, ou <c>null</c> em conflito.
    /// </summary>
    Task<EvaluationRun?> EnqueueAsync(EvaluationRun run, CancellationToken ct = default);

    /// <summary>
    /// Compare-and-swap: atualiza status só se atualmente em <paramref name="from"/>.
    /// Evita last-writer-wins em cancel mid-run e Running→Completed/Failed.
    /// </summary>
    Task<bool> TryTransitionStatusAsync(
        string runId,
        EvaluationRunStatus from,
        EvaluationRunStatus to,
        string? lastError = null,
        CancellationToken ct = default);

    Task TouchHeartbeatAsync(string runId, CancellationToken ct = default);

    /// <summary>
    /// Última run <c>Completed</c> no mesmo tuple (AgentDefinitionId, TestSetVersionId,
    /// EvaluatorConfigVersionId) — usado para determinar BaselineRunId em regression detection.
    /// </summary>
    Task<EvaluationRun?> FindBaselineAsync(
        string agentDefinitionId,
        string testSetVersionId,
        string evaluatorConfigVersionId,
        CancellationToken ct = default);

    /// <summary>
    /// Próxima run <c>Pending</c> ordenada por (Priority ASC, CreatedAt ASC).
    /// </summary>
    Task<EvaluationRun?> DequeuePendingAsync(CancellationToken ct = default);

    /// <summary>Runs <c>Running</c> sem heartbeat há mais que <paramref name="staleAfter"/>.</summary>
    Task<IReadOnlyList<EvaluationRun>> ListStaleRunningAsync(TimeSpan staleAfter, CancellationToken ct = default);
}
