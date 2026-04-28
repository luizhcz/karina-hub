namespace EfsAiHub.Core.Agents.Evaluation;

public interface IEvaluatorConfigRepository
{
    Task<EvaluatorConfig?> GetByIdAsync(string id, CancellationToken ct = default);

    Task<EvaluatorConfig?> GetByAgentDefinitionAsync(string agentDefinitionId, CancellationToken ct = default);

    Task<IReadOnlyList<EvaluatorConfig>> ListByAgentDefinitionAsync(string agentDefinitionId, CancellationToken ct = default);

    Task<EvaluatorConfig> UpsertAsync(EvaluatorConfig config, CancellationToken ct = default);

    Task SetCurrentVersionAsync(string configId, string evaluatorConfigVersionId, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);
}
