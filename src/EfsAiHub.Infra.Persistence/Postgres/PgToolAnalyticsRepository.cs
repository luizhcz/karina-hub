using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Agregações de tool_invocations por projeto. Mesmo padrão de
/// <see cref="PgProjectAnalyticsRepository"/>: <c>SqlQueryRaw</c> com
/// parâmetros posicionais, INNER JOIN com <c>v_production_executions</c> pra
/// remover sandbox. p50/p95 via <c>percentile_cont</c> (calculo exato sobre
/// todas as durações do período, sem amostragem).
///
/// Sem cache Redis no V1 — o volume da tabela é baixo (índice por ExecutionId
/// + AgentId + ToolName cobre todos os predicates). Se p95 da query ficar
/// >50ms em prod, replicar o padrão de <see cref="PgProjectAnalyticsRepository._cache"/>
/// (TTL 30min, invalidação por projeto).
/// </summary>
public sealed class PgToolAnalyticsRepository : IToolAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgToolAnalyticsRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<ToolUsageOverview> GetToolUsageOverviewAsync(
        string projectId,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Agregado total — sem GROUP BY. percentile_cont opera sobre todas as
        // chamadas do período pra dar p95 "consolidado" do projeto, não a
        // média de p95s das tools.
        var aggSql = """
            SELECT
                COALESCE(COUNT(*), 0)::bigint                                       AS "TotalCalls",
                COALESCE(SUM(CASE WHEN ti."Success" THEN 1 ELSE 0 END), 0)::bigint  AS "TotalSucceeded",
                COALESCE(SUM(CASE WHEN NOT ti."Success" THEN 1 ELSE 0 END), 0)::bigint AS "TotalFailed",
                COALESCE(COUNT(DISTINCT ti."ToolName"), 0)::int                     AS "DistinctTools",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY ti."DurationMs"), 0)::float8 AS "P95DurationMs"
            FROM aihub.tool_invocations ti
            INNER JOIN aihub.v_production_executions we ON we."ExecutionId" = ti."ExecutionId"
            WHERE we."ProjectId" = {0}
              AND ti."CreatedAt" BETWEEN {1} AND {2}
            """;

        var agg = await db.Database.SqlQueryRaw<OverviewAggRaw>(aggSql, projectId, from, to)
            .ToListAsync(ct);
        var a = agg.FirstOrDefault() ?? new OverviewAggRaw();

        // Linhas detalhadas por tool. ORDER BY calls DESC alinha com o que o
        // dashboard renderiza primeiro (tool mais usada no topo).
        var toolsSql = """
            SELECT
                ti."ToolName"                                                       AS "ToolName",
                COUNT(*)::bigint                                                    AS "Calls",
                SUM(CASE WHEN ti."Success" THEN 1 ELSE 0 END)::bigint               AS "Succeeded",
                SUM(CASE WHEN NOT ti."Success" THEN 1 ELSE 0 END)::bigint           AS "Failed",
                AVG(ti."DurationMs")::float8                                        AS "AvgDurationMs",
                percentile_cont(0.5)  WITHIN GROUP (ORDER BY ti."DurationMs")::float8 AS "P50DurationMs",
                percentile_cont(0.95) WITHIN GROUP (ORDER BY ti."DurationMs")::float8 AS "P95DurationMs",
                MAX(ti."DurationMs")::float8                                        AS "MaxDurationMs",
                COUNT(DISTINCT ti."AgentId")::int                                   AS "DistinctAgents"
            FROM aihub.tool_invocations ti
            INNER JOIN aihub.v_production_executions we ON we."ExecutionId" = ti."ExecutionId"
            WHERE we."ProjectId" = {0}
              AND ti."CreatedAt" BETWEEN {1} AND {2}
            GROUP BY ti."ToolName"
            ORDER BY COUNT(*) DESC
            """;

        var rows = await db.Database.SqlQueryRaw<ToolRowRaw>(toolsSql, projectId, from, to)
            .ToListAsync(ct);

        var resolved = a.TotalSucceeded + a.TotalFailed;
        var errorRate = resolved > 0 ? (double)a.TotalFailed / resolved : 0d;

        return new ToolUsageOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            TotalCalls = a.TotalCalls,
            TotalSucceeded = a.TotalSucceeded,
            TotalFailed = a.TotalFailed,
            DistinctTools = a.DistinctTools,
            ErrorRate = errorRate,
            P95DurationMs = a.P95DurationMs,
            Tools = rows.Select(r => new ToolUsageRow
            {
                ToolName = r.ToolName,
                Calls = r.Calls,
                Succeeded = r.Succeeded,
                Failed = r.Failed,
                ErrorRate = (r.Succeeded + r.Failed) > 0
                    ? (double)r.Failed / (r.Succeeded + r.Failed)
                    : 0d,
                AvgDurationMs = r.AvgDurationMs,
                P50DurationMs = r.P50DurationMs,
                P95DurationMs = r.P95DurationMs,
                MaxDurationMs = r.MaxDurationMs,
                DistinctAgents = r.DistinctAgents,
            }).ToArray(),
        };
    }

    public async Task<IReadOnlyList<ToolTimeseriesBucket>> GetToolTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string groupBy,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Whitelist do groupBy — defesa em profundidade contra SQL injection
        // por concatenação (date_trunc não aceita parâmetro). Fallback "day"
        // pra ser tolerante a query param vazio/inválido.
        var truncUnit = groupBy?.Trim().ToLowerInvariant() switch
        {
            "hour" => "hour",
            _ => "day",
        };

        // $$ + {{ }} é a sintaxe de raw-string interpolated com 2 dollars —
        // dentro do conteúdo, `{0}` vira literal (chega no SqlQueryRaw como
        // placeholder posicional) e `{{truncUnit}}` é a substituição C#.
        var sql = $$"""
            SELECT
                date_trunc('{{truncUnit}}', ti."CreatedAt")::timestamptz           AS "Bucket",
                COUNT(*)::bigint                                                   AS "Calls",
                SUM(CASE WHEN ti."Success" THEN 1 ELSE 0 END)::bigint              AS "Succeeded",
                SUM(CASE WHEN NOT ti."Success" THEN 1 ELSE 0 END)::bigint          AS "Failed",
                COALESCE(AVG(ti."DurationMs"), 0)::float8                          AS "AvgDurationMs",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY ti."DurationMs"), 0)::float8 AS "P95DurationMs"
            FROM aihub.tool_invocations ti
            INNER JOIN aihub.v_production_executions we ON we."ExecutionId" = ti."ExecutionId"
            WHERE we."ProjectId" = {0}
              AND ti."CreatedAt" BETWEEN {1} AND {2}
            GROUP BY date_trunc('{{truncUnit}}', ti."CreatedAt")
            ORDER BY date_trunc('{{truncUnit}}', ti."CreatedAt") ASC
            """;

        var rows = await db.Database.SqlQueryRaw<BucketRaw>(sql, projectId, from, to)
            .ToListAsync(ct);

        return rows.Select(r => new ToolTimeseriesBucket
        {
            Bucket = r.Bucket,
            Calls = r.Calls,
            Succeeded = r.Succeeded,
            Failed = r.Failed,
            AvgDurationMs = r.AvgDurationMs,
            P95DurationMs = r.P95DurationMs,
        }).ToArray();
    }

    // DTOs internos usados só pra projeção do SqlQueryRaw. Setters publicly
    // settable porque o materializador do EF (sem keyless mapping) usa Activator.
    private sealed class OverviewAggRaw
    {
        public long TotalCalls { get; set; }
        public long TotalSucceeded { get; set; }
        public long TotalFailed { get; set; }
        public int DistinctTools { get; set; }
        public double P95DurationMs { get; set; }
    }

    private sealed class ToolRowRaw
    {
        public string ToolName { get; set; } = "";
        public long Calls { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public double AvgDurationMs { get; set; }
        public double P50DurationMs { get; set; }
        public double P95DurationMs { get; set; }
        public double MaxDurationMs { get; set; }
        public int DistinctAgents { get; set; }
    }

    private sealed class BucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Calls { get; set; }
        public long Succeeded { get; set; }
        public long Failed { get; set; }
        public double AvgDurationMs { get; set; }
        public double P95DurationMs { get; set; }
    }
}
