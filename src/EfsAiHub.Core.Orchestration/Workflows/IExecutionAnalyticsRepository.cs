namespace EfsAiHub.Core.Orchestration.Workflows;

public record ExecutionSummary(
    int Total,
    int Completed,
    int Failed,
    int Cancelled,
    int Running,
    int Pending,
    double SuccessRate,
    double AvgDurationMs,
    double P50Ms,
    double P95Ms);

public record ExecutionTimeseriesBucket(
    string Bucket,
    int Total,
    int Completed,
    int Failed,
    double AvgDurationMs);

/// <summary>
/// Breakdown de falhas por <c>ErrorCategory</c>. Espelha a tag <c>error.category</c>
/// emitida pela métrica <c>workflows.failed</c> — permite dashboard de UI consumir
/// o mesmo eixo que o OpenTelemetry expõe em Prometheus/Grafana.
/// </summary>
public record ExecutionFailureBreakdown(
    /// <summary>Nome da categoria (ex: Timeout, BudgetExceeded, HitlRejected, Unknown).</summary>
    string Category,
    /// <summary>Quantidade de execuções que falharam com essa categoria no período.</summary>
    int Count);

public interface IExecutionAnalyticsRepository
{
    /// <summary>
    /// Métricas agregadas de execuções no período, escopadas por
    /// <paramref name="projectId"/> e filtradas pra excluir sandbox (usa
    /// <c>aihub.v_production_executions</c>). <paramref name="workflowId"/>
    /// permite drill-down opcional pra um workflow específico.
    /// </summary>
    Task<ExecutionSummary> GetSummaryAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string? workflowId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionTimeseriesBucket>> GetTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string? workflowId = null,
        string groupBy = "hour",
        CancellationToken ct = default);

    /// <summary>
    /// Conta execuções com <c>Status=Failed</c> agrupadas por <c>ErrorCategory</c>
    /// no intervalo. Ordenado por count desc (top categorias primeiro).
    /// NULL ErrorCategory colapsa em "Unknown".
    /// </summary>
    Task<IReadOnlyList<ExecutionFailureBreakdown>> GetFailureBreakdownAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string? workflowId = null,
        CancellationToken ct = default);
}
