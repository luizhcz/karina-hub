namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluationTestCaseRepository
{
    Task<EvaluationTestCase?> GetByIdAsync(string caseId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationTestCase>> ListByVersionAsync(
        string testSetVersionId,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default);

    /// <summary>Subset eval por tag — retorna cases que contêm pelo menos uma tag de <paramref name="tags"/>.</summary>
    Task<IReadOnlyList<EvaluationTestCase>> ListByVersionAndTagsAsync(
        string testSetVersionId,
        IReadOnlyList<string> tags,
        CancellationToken ct = default);

    Task<int> CountByVersionAsync(string testSetVersionId, CancellationToken ct = default);
}
