namespace EfsAiHub.Core.Orchestration.Workflows;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default);
    Task<WorkflowDefinition> UpsertAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task<bool> ExistsAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Lista workflows visíveis para um projeto: workflows do projeto + workflows globais.
    /// </summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListVisibleAsync(string projectId, CancellationToken ct = default);
}
