namespace EfsAiHub.Core.Agents.Evaluation;

/// <summary>
/// Stats agregadas de uma TestSetVersion — quantidade de cases e revision.
/// Usado pela listagem do controller pra enriquecer o response sem N+1.
/// </summary>
public sealed record TestSetVersionStats(int CaseCount, int Revision);

public interface IEvaluationTestSetRepository
{
    Task<EvaluationTestSet?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluationTestSet>> ListByProjectAsync(
        string projectId,
        bool includeGlobal = true,
        CancellationToken ct = default);

    /// <summary>
    /// Retorna stats por <c>TestSetVersionId</c> (count de cases + revision). Usado
    /// pelo controller pra hidratar o list response sem N+1. IDs ausentes na
    /// resposta indicam version sem cases ou inexistente — caller trata como 0.
    /// </summary>
    Task<IReadOnlyDictionary<string, TestSetVersionStats>> GetStatsForVersionsAsync(
        IEnumerable<string> testSetVersionIds,
        CancellationToken ct = default);

    Task<EvaluationTestSet> UpsertAsync(EvaluationTestSet testSet, CancellationToken ct = default);

    /// <summary>Atualiza o ponteiro <c>CurrentVersionId</c> (rollback determinístico).</summary>
    Task SetCurrentVersionAsync(string testSetId, string testSetVersionId, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
