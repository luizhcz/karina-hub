using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Implementação Postgres do analytics da fila standalone. Mesmo padrão
/// dos demais repos analytics: <c>SqlQueryRaw</c> parametrizado posicionalmente,
/// p95 via <c>percentile_cont</c> (exato, sem amostragem), production-only
/// implícito porque <c>background_response_jobs</c> só contém jobs reais —
/// não há sandbox standalone gravando aqui (sandboxes vão pelo Chat Path).
/// </summary>
public sealed class PgStandaloneJobAnalyticsRepository : IStandaloneJobAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgStandaloneJobAnalyticsRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<StandaloneJobOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Agregado total — toda métrica deriva de uma única passagem pela tabela.
        // FILTER (WHERE ...) evita N queries separadas; percentile_cont sobre os
        // EXTRACT(EPOCH ...) calcula latências em ms direto no Postgres.
        var aggSql = """
            SELECT
                COUNT(*)::bigint                                                      AS "TotalJobs",
                COUNT(*) FILTER (WHERE "Status" = 'Completed')::bigint                AS "Completed",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                   AS "Failed",
                COUNT(*) FILTER (WHERE "Status" = 'Cancelled')::bigint                AS "Cancelled",
                COUNT(*) FILTER (WHERE "Status" = 'Queued')::bigint                   AS "Queued",
                COUNT(*) FILTER (WHERE "Status" = 'Running')::bigint                  AS "Running",
                COALESCE(SUM("Attempt"), 0)::bigint                                   AS "TotalAttempts",
                COUNT(*) FILTER (WHERE "Attempt" > 1)::bigint                         AS "JobsWithRetry",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("StartedAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "StartedAt" IS NOT NULL), 0)::float8                AS "P95QueueMs",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("CompletedAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "Status" = 'Completed' AND "CompletedAt" IS NOT NULL), 0)::float8 AS "P95TotalMs"
            FROM aihub.background_response_jobs
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            """;

        var agg = await db.Database.SqlQueryRaw<OverviewAggRaw>(aggSql, projectId, from, to).ToListAsync(ct);
        var a = agg.FirstOrDefault() ?? new OverviewAggRaw();

        // Quebra por WorkflowId — top workflows pela carga. NULL WorkflowId
        // (jobs órfãos / migração legacy) colapsa em "unknown" pra que a UI
        // não bata em chave duplicada.
        var workflowsSql = """
            SELECT
                COALESCE(NULLIF("WorkflowId", ''), 'unknown')                           AS "WorkflowId",
                COUNT(*)::bigint                                                        AS "Total",
                COUNT(*) FILTER (WHERE "Status" = 'Completed')::bigint                  AS "Completed",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                     AS "Failed",
                COALESCE(SUM("Attempt"), 0)::bigint                                     AS "TotalAttempts",
                COALESCE(AVG(EXTRACT(EPOCH FROM ("CompletedAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "Status" = 'Completed' AND "CompletedAt" IS NOT NULL), 0)::float8 AS "AvgTotalMs",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("CompletedAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "Status" = 'Completed' AND "CompletedAt" IS NOT NULL), 0)::float8 AS "P95TotalMs"
            FROM aihub.background_response_jobs
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY COALESCE(NULLIF("WorkflowId", ''), 'unknown')
            ORDER BY COUNT(*) DESC
            LIMIT 25
            """;

        var workflows = await db.Database.SqlQueryRaw<WorkflowRowRaw>(workflowsSql, projectId, from, to).ToListAsync(ct);

        var resolved = a.Completed + a.Failed;
        return new StandaloneJobOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            TotalJobs = a.TotalJobs,
            Completed = a.Completed,
            Failed = a.Failed,
            Cancelled = a.Cancelled,
            Queued = a.Queued,
            Running = a.Running,
            SuccessRate = resolved > 0 ? (double)a.Completed / resolved : 0d,
            TotalAttempts = a.TotalAttempts,
            RetryRate = a.TotalJobs > 0 ? (double)a.JobsWithRetry / a.TotalJobs : 0d,
            P95QueueMs = a.P95QueueMs,
            P95TotalMs = a.P95TotalMs,
            Workflows = workflows.Select(w => new StandaloneJobByWorkflow
            {
                WorkflowId = w.WorkflowId,
                Total = w.Total,
                Completed = w.Completed,
                Failed = w.Failed,
                SuccessRate = (w.Completed + w.Failed) > 0
                    ? (double)w.Completed / (w.Completed + w.Failed)
                    : 0d,
                TotalAttempts = w.TotalAttempts,
                AvgTotalMs = w.AvgTotalMs,
                P95TotalMs = w.P95TotalMs,
            }).ToArray(),
        };
    }

    public async Task<IReadOnlyList<StandaloneJobTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var truncUnit = groupBy?.Trim().ToLowerInvariant() switch
        {
            "hour" => "hour",
            _ => "day",
        };

        var sql = $$"""
            SELECT
                date_trunc('{{truncUnit}}', "CreatedAt")::timestamptz                  AS "Bucket",
                COUNT(*)::bigint                                                       AS "Created",
                COUNT(*) FILTER (WHERE "Status" = 'Completed')::bigint                 AS "Completed",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                    AS "Failed",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("StartedAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "StartedAt" IS NOT NULL), 0)::float8                 AS "P95QueueMs"
            FROM aihub.background_response_jobs
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY date_trunc('{{truncUnit}}', "CreatedAt")
            ORDER BY date_trunc('{{truncUnit}}', "CreatedAt") ASC
            """;

        var rows = await db.Database.SqlQueryRaw<BucketRaw>(sql, projectId, from, to).ToListAsync(ct);

        return rows.Select(r => new StandaloneJobTimeseriesBucket
        {
            Bucket = r.Bucket,
            Created = r.Created,
            Completed = r.Completed,
            Failed = r.Failed,
            P95QueueMs = r.P95QueueMs,
        }).ToArray();
    }

    private sealed class OverviewAggRaw
    {
        public long TotalJobs { get; set; }
        public long Completed { get; set; }
        public long Failed { get; set; }
        public long Cancelled { get; set; }
        public long Queued { get; set; }
        public long Running { get; set; }
        public long TotalAttempts { get; set; }
        public long JobsWithRetry { get; set; }
        public double P95QueueMs { get; set; }
        public double P95TotalMs { get; set; }
    }

    private sealed class WorkflowRowRaw
    {
        public string WorkflowId { get; set; } = "";
        public long Total { get; set; }
        public long Completed { get; set; }
        public long Failed { get; set; }
        public long TotalAttempts { get; set; }
        public double AvgTotalMs { get; set; }
        public double P95TotalMs { get; set; }
    }

    private sealed class BucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Created { get; set; }
        public long Completed { get; set; }
        public long Failed { get; set; }
        public double P95QueueMs { get; set; }
    }
}
