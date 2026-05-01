using EfsAiHub.Core.Abstractions.Execution;

namespace EfsAiHub.Platform.Runtime.Interfaces;

public interface IWorkflowService
{
    Task<WorkflowDefinition> CreateAsync(WorkflowDefinition definition, CancellationToken ct = default);
    Task<WorkflowDefinition?> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowDefinition>> ListAsync(CancellationToken ct = default);
    Task<WorkflowDefinition> UpdateAsync(WorkflowDefinition definition, CancellationToken ct = default);

    /// <summary>
    /// Muda a visibilidade ('project' | 'global') de um workflow existente.
    /// Retorna a definição atualizada. Lança <see cref="KeyNotFoundException"/> se workflow não
    /// existir, e <see cref="ArgumentException"/> se newVisibility for inválido.
    /// </summary>
    Task<WorkflowDefinition> UpdateVisibilityAsync(string id, string newVisibility, CancellationToken ct = default);

    Task DeleteAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Enfileira um workflow para execução em background.
    /// Retorna o executionId imediatamente (fire-and-forget).
    /// </summary>
    Task<string> TriggerAsync(string workflowId, string? inputPayload, Dictionary<string, string>? metadata = null, ExecutionSource source = ExecutionSource.Api, ExecutionMode mode = ExecutionMode.Production, CancellationToken ct = default);

    Task<WorkflowExecution?> GetExecutionAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetExecutionsAsync(string workflowId, int page = 1, int pageSize = 20, string? status = null, CancellationToken ct = default);
    Task<(IReadOnlyList<WorkflowExecution> Items, int Total)> GetAllExecutionsAsync(string? workflowId = null, string? status = null, DateTime? from = null, DateTime? to = null, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task CancelExecutionAsync(string executionId, CancellationToken ct = default);

    Task<(bool IsValid, IReadOnlyList<string> Errors)> ValidateAsync(
        WorkflowDefinition definition, CancellationToken ct = default);

    // ── Catalog ───────────────────────────────────────────────────────────
    /// <summary>Lista workflows visíveis para o projeto atual (project + global).</summary>
    Task<IReadOnlyList<WorkflowDefinition>> ListVisibleAsync(CancellationToken ct = default);

    /// <summary>Clona uma WorkflowDefinition para o projeto atual com novo ID.</summary>
    Task<WorkflowDefinition> CloneAsync(string sourceWorkflowId, string? newId = null, CancellationToken ct = default);

    // ── Versioning ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<WorkflowVersion>> ListVersionsAsync(string workflowId, CancellationToken ct = default);
    Task<WorkflowVersion?> GetVersionAsync(string versionId, CancellationToken ct = default);
    Task<WorkflowDefinition> RollbackAsync(string workflowId, string versionId, CancellationToken ct = default);
}
