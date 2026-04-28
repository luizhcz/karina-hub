namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluationTestSetVersionRepository
{
    Task<EvaluationTestSetVersion?> GetByIdAsync(string testSetVersionId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationTestSetVersion>> ListByTestSetAsync(string testSetId, CancellationToken ct = default);

    Task<int> GetNextRevisionAsync(string testSetId, CancellationToken ct = default);

    /// <summary>
    /// Persiste version + cases atomicamente. Idempotente por ContentHash:
    /// (TestSetId, ContentHash) em status != Deprecated retorna a existente (no-op).
    /// </summary>
    Task<EvaluationTestSetVersion> AppendAsync(
        EvaluationTestSetVersion version,
        IReadOnlyList<EvaluationTestCase> cases,
        CancellationToken ct = default);

    Task SetStatusAsync(string testSetVersionId, TestSetVersionStatus status, CancellationToken ct = default);
}
