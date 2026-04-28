namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluatorConfigVersionRepository
{
    Task<EvaluatorConfigVersion?> GetByIdAsync(string evaluatorConfigVersionId, CancellationToken ct = default);

    Task<EvaluatorConfigVersion?> GetCurrentAsync(string evaluatorConfigId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluatorConfigVersion>> ListByConfigAsync(string evaluatorConfigId, CancellationToken ct = default);

    Task<int> GetNextRevisionAsync(string evaluatorConfigId, CancellationToken ct = default);

    /// <summary>
    /// Persiste snapshot. Idempotente por ContentHash: se a última revision
    /// já carrega esse hash, retorna-a (no-op).
    /// </summary>
    Task<EvaluatorConfigVersion> AppendAsync(EvaluatorConfigVersion version, CancellationToken ct = default);

    Task SetStatusAsync(string evaluatorConfigVersionId, EvaluatorConfigVersionStatus status, CancellationToken ct = default);
}
