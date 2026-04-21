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

public interface IExecutionAnalyticsRepository
{
    Task<ExecutionSummary> GetSummaryAsync(
        DateTime from,
        DateTime to,
        string? workflowId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ExecutionTimeseriesBucket>> GetTimeseriesAsync(
        DateTime from,
        DateTime to,
        string? workflowId = null,
        string groupBy = "hour",
        CancellationToken ct = default);
}
