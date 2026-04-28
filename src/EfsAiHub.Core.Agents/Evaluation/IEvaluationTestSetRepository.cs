namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluationTestSetRepository
{
    Task<EvaluationTestSet?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationTestSet>> ListByProjectAsync(
        string projectId,
        bool includeGlobal = true,
        CancellationToken ct = default);

    Task<EvaluationTestSet> UpsertAsync(EvaluationTestSet testSet, CancellationToken ct = default);

    /// <summary>Atualiza o ponteiro <c>CurrentVersionId</c> (rollback determinístico).</summary>
    Task SetCurrentVersionAsync(string testSetId, string testSetVersionId, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
