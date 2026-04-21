namespace EfsAiHub.Core.Orchestration.Workflows;

/// <summary>
/// Repositório append-only de snapshots imutáveis de WorkflowVersion.
/// Padrão idêntico ao IAgentVersionRepository.
/// </summary>
public interface IWorkflowVersionRepository
{
    Task<WorkflowVersion?> GetByIdAsync(string workflowVersionId, CancellationToken ct = default);

    Task<WorkflowVersion?> GetCurrentAsync(string workflowDefinitionId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkflowVersion>> ListByDefinitionAsync(string workflowDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Persiste um novo snapshot. Idempotente por ContentHash — se o hash já existe
    /// na última revision, retorna a existente (no-op).
    /// </summary>
    Task<WorkflowVersion> AppendAsync(WorkflowVersion version, CancellationToken ct = default);

    Task<int> GetNextRevisionAsync(string workflowDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Recupera o snapshot da WorkflowDefinition persistido para uma versão específica.
    /// Usado no rollback para restaurar o estado exato da definição.
    /// </summary>
    Task<WorkflowDefinition?> GetDefinitionSnapshotAsync(string workflowVersionId, CancellationToken ct = default);
}
