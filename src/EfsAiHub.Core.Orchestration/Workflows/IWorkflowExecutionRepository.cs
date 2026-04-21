
namespace EfsAiHub.Core.Orchestration.Workflows;

public interface IWorkflowExecutionRepository
{
    Task<WorkflowExecution?> GetByIdAsync(string executionId, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetByIdsAsync(IEnumerable<string> executionIds, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowIdAsync(string workflowId, int page = 1, int pageSize = 20, string? status = null, CancellationToken ct = default);
    Task<WorkflowExecution> CreateAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<WorkflowExecution> UpdateAsync(WorkflowExecution execution, CancellationToken ct = default);
    Task<IReadOnlyList<WorkflowExecution>> GetActiveExecutionsAsync(int maxLimit = 1000, CancellationToken ct = default);

    /// <summary>
    /// Lista execuções com Status=Paused em lotes paginados (ordenadas por StartedAt asc).
    /// Usado pelo HitlRecoveryService para evitar thundering herd no startup.
    /// </summary>
    Task<IReadOnlyList<WorkflowExecution>> GetPausedExecutionsPagedAsync(
        int offset,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>Conta execuções com Status=Paused (para métrica de backlog de recovery).</summary>
    Task<int> CountPausedAsync(CancellationToken ct = default);

    /// <summary>
    /// Conta execuções com Status=Running para o workflow especificado.
    /// Usado como guarda distribuída de concorrência em ambientes multi-instância.
    /// </summary>
    Task<int> CountRunningAsync(string workflowId, CancellationToken ct = default);

    /// <summary>
    /// Lista execuções de todos os workflows com filtros opcionais.
    /// Substitui o padrão N×4 de polling por workflow no frontend.
    /// </summary>
    Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(
        string? workflowId = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<int> CountAsync(
        string? workflowId = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
