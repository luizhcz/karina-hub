using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Analytics repository que roda puramente no pool "reporting" via raw Npgsql.
/// P50/P95 são calculados no Postgres (percentile_cont) — reaproveita o índice
/// composto IX_workflow_executions_WorkflowId_Status_StartedAt.
/// </summary>
public class PgExecutionAnalyticsRepository : IExecutionAnalyticsRepository
{
    private readonly NpgsqlDataSource _reporting;

    public PgExecutionAnalyticsRepository([FromKeyedServices("reporting")] NpgsqlDataSource reporting)
        => _reporting = reporting;

    public async Task<ExecutionSummary> GetSummaryAsync(
        DateTime from,
        DateTime to,
        string? workflowId = null,
        CancellationToken ct = default)
    {
        const string sql = @"
SELECT
    COUNT(*)                                                                      AS total,
    COUNT(*) FILTER (WHERE ""Status"" = 'Completed')                              AS completed,
    COUNT(*) FILTER (WHERE ""Status"" = 'Failed')                                 AS failed,
    COUNT(*) FILTER (WHERE ""Status"" = 'Cancelled')                              AS cancelled,
    COUNT(*) FILTER (WHERE ""Status"" = 'Running')                                AS running,
    COUNT(*) FILTER (WHERE ""Status"" = 'Pending')                                AS pending,
    COALESCE(AVG(EXTRACT(EPOCH FROM (""CompletedAt"" - ""StartedAt"")) * 1000.0)
        FILTER (WHERE ""Status"" = 'Completed' AND ""CompletedAt"" IS NOT NULL), 0) AS avg_ms,
    COALESCE(percentile_cont(0.5) WITHIN GROUP (
        ORDER BY EXTRACT(EPOCH FROM (""CompletedAt"" - ""StartedAt"")) * 1000.0)
        FILTER (WHERE ""Status"" = 'Completed' AND ""CompletedAt"" IS NOT NULL), 0) AS p50_ms,
    COALESCE(percentile_cont(0.95) WITHIN GROUP (
        ORDER BY EXTRACT(EPOCH FROM (""CompletedAt"" - ""StartedAt"")) * 1000.0)
        FILTER (WHERE ""Status"" = 'Completed' AND ""CompletedAt"" IS NOT NULL), 0) AS p95_ms
FROM workflow_executions
WHERE ""StartedAt"" >= @from AND ""StartedAt"" <= @to
  AND (@workflowId IS NULL OR ""WorkflowId"" = @workflowId);";

        await using var conn = await _reporting.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.Add(new NpgsqlParameter("workflowId", NpgsqlDbType.Varchar) { Value = (object?)workflowId ?? DBNull.Value });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new ExecutionSummary(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        var total = (int)reader.GetInt64(0);
        var completed = (int)reader.GetInt64(1);
        var failed = (int)reader.GetInt64(2);
        var cancelled = (int)reader.GetInt64(3);
        var running = (int)reader.GetInt64(4);
        var pending = (int)reader.GetInt64(5);
        var avgMs = Convert.ToDouble(reader.GetValue(6));
        var p50Ms = Convert.ToDouble(reader.GetValue(7));
        var p95Ms = Convert.ToDouble(reader.GetValue(8));

        var finalized = completed + failed;
        var successRate = finalized > 0 ? (double)completed / finalized * 100 : 0;

        return new ExecutionSummary(
            total, completed, failed, cancelled, running, pending,
            Math.Round(successRate, 1),
            Math.Round(avgMs),
            Math.Round(p50Ms),
            Math.Round(p95Ms));
    }

    public async Task<IReadOnlyList<ExecutionTimeseriesBucket>> GetTimeseriesAsync(
        DateTime from,
        DateTime to,
        string? workflowId = null,
        string groupBy = "hour",
        CancellationToken ct = default)
    {
        var trunc = groupBy == "day" ? "day" : "hour";

        var sql = $@"
SELECT
    date_trunc('{trunc}', ""StartedAt"") AS bucket,
    COUNT(*)                                                     AS total,
    COUNT(*) FILTER (WHERE ""Status"" = 'Completed')             AS completed,
    COUNT(*) FILTER (WHERE ""Status"" = 'Failed')                AS failed,
    COALESCE(AVG(EXTRACT(EPOCH FROM (""CompletedAt"" - ""StartedAt"")) * 1000.0)
        FILTER (WHERE ""Status"" = 'Completed' AND ""CompletedAt"" IS NOT NULL), 0) AS avg_ms
FROM workflow_executions
WHERE ""StartedAt"" >= @from AND ""StartedAt"" <= @to
  AND (@workflowId IS NULL OR ""WorkflowId"" = @workflowId)
GROUP BY bucket
ORDER BY bucket;";

        await using var conn = await _reporting.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.Add(new NpgsqlParameter("workflowId", NpgsqlDbType.Varchar) { Value = (object?)workflowId ?? DBNull.Value });

        var buckets = new List<ExecutionTimeseriesBucket>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var bucket = DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc);
            var total = (int)reader.GetInt64(1);
            var completed = (int)reader.GetInt64(2);
            var failed = (int)reader.GetInt64(3);
            var avgMs = Convert.ToDouble(reader.GetValue(4));

            buckets.Add(new ExecutionTimeseriesBucket(
                Bucket: bucket.ToString("O"),
                Total: total,
                Completed: completed,
                Failed: failed,
                AvgDurationMs: Math.Round(avgMs)));
        }

        return buckets;
    }

    public async Task<IReadOnlyList<ExecutionFailureBreakdown>> GetFailureBreakdownAsync(
        DateTime from,
        DateTime to,
        string? workflowId = null,
        CancellationToken ct = default)
    {
        // WorkflowExecutionRow persiste WorkflowExecution serializado em Data (JSONB).
        // ErrorCategory não é coluna dedicada — extraímos via operador ->> que navega o JSONB.
        // COALESCE+NULLIF garante 'Unknown' para execuções Failed sem ErrorCategory (edge case).
        // Filtra apenas Status=Failed (workflows.cancelled é counter separado).
        // Ordena por count DESC (top categorias primeiro).
        const string sql = @"
SELECT
    COALESCE(NULLIF(""Data""::jsonb ->> 'ErrorCategory', ''), 'Unknown') AS category,
    COUNT(*)                                                             AS count
FROM workflow_executions
WHERE ""Status"" = 'Failed'
  AND ""StartedAt"" >= @from AND ""StartedAt"" <= @to
  AND (@workflowId IS NULL OR ""WorkflowId"" = @workflowId)
GROUP BY COALESCE(NULLIF(""Data""::jsonb ->> 'ErrorCategory', ''), 'Unknown')
ORDER BY count DESC;";

        await using var conn = await _reporting.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.Add(new NpgsqlParameter("workflowId", NpgsqlDbType.Varchar) { Value = (object?)workflowId ?? DBNull.Value });

        var breakdown = new List<ExecutionFailureBreakdown>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            breakdown.Add(new ExecutionFailureBreakdown(
                Category: reader.GetString(0),
                Count: (int)reader.GetInt64(1)));
        }
        return breakdown;
    }
}
