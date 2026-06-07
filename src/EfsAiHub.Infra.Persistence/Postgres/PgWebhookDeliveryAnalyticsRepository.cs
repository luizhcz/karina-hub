using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Analytics de webhook_deliveries. Host extraído de Url via substring()
/// + regex pra evitar carregar parsing pra a aplicação. Bucket por código HTTP
/// gerado via CASE de divisão inteira (LastResponseCode / 100) — agrupa em
/// classes 2xx/4xx/5xx + sentinela "no-response" pra timeouts/DNS errors.
/// </summary>
public sealed class PgWebhookDeliveryAnalyticsRepository : IWebhookDeliveryAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgWebhookDeliveryAnalyticsRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<WebhookDeliveryOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var aggSql = """
            SELECT
                COUNT(*)::bigint                                                      AS "TotalDeliveries",
                COUNT(*) FILTER (WHERE "Status" = 'Delivered')::bigint                AS "Delivered",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                   AS "Failed",
                COUNT(*) FILTER (WHERE "Status" = 'Pending')::bigint                  AS "Pending",
                COUNT(*) FILTER (WHERE "Status" = 'Delivering')::bigint               AS "Delivering",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("DeliveredAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "Status" = 'Delivered' AND "DeliveredAt" IS NOT NULL), 0)::float8 AS "P95DeliveryMs"
            FROM aihub.webhook_deliveries
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            """;

        var agg = await db.Database.SqlQueryRaw<OverviewAggRaw>(aggSql, projectId, from, to).ToListAsync(ct);
        var a = agg.FirstOrDefault() ?? new OverviewAggRaw();

        // Bucketização por classe HTTP — 2xx/4xx/5xx + "no-response" (LastResponseCode IS NULL).
        // Granularidade ideal pra dashboard: agrupar 400 e 404 em "4xx" responde 80% das perguntas
        // sem inflar a cardinalidade.
        var statusSql = """
            SELECT
                CASE
                    WHEN "LastResponseCode" IS NULL                        THEN 'no-response'
                    WHEN "LastResponseCode" BETWEEN 200 AND 299            THEN '2xx'
                    WHEN "LastResponseCode" BETWEEN 300 AND 399            THEN '3xx'
                    WHEN "LastResponseCode" BETWEEN 400 AND 499            THEN '4xx'
                    WHEN "LastResponseCode" BETWEEN 500 AND 599            THEN '5xx'
                    ELSE 'other'
                END                                                                    AS "Bucket",
                COUNT(*)::bigint                                                       AS "Count"
            FROM aihub.webhook_deliveries
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY 1
            ORDER BY COUNT(*) DESC
            """;

        var statuses = await db.Database.SqlQueryRaw<StatusRowRaw>(statusSql, projectId, from, to).ToListAsync(ct);

        // Top hosts. substring(url, 'https?://([^/:]+)') extrai o host. Fallback
        // pra "unknown" quando regex não bate (URL mal-formada — não deveria acontecer
        // mas defensivo).
        var hostsSql = """
            SELECT
                COALESCE(substring("Url" FROM 'https?://([^/:]+)'), 'unknown')         AS "Host",
                COUNT(*)::bigint                                                       AS "Total",
                COUNT(*) FILTER (WHERE "Status" = 'Delivered')::bigint                 AS "Delivered",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                    AS "Failed"
            FROM aihub.webhook_deliveries
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY COALESCE(substring("Url" FROM 'https?://([^/:]+)'), 'unknown')
            ORDER BY COUNT(*) DESC
            LIMIT 20
            """;

        var hosts = await db.Database.SqlQueryRaw<HostRowRaw>(hostsSql, projectId, from, to).ToListAsync(ct);

        var resolved = a.Delivered + a.Failed;
        return new WebhookDeliveryOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            TotalDeliveries = a.TotalDeliveries,
            Delivered = a.Delivered,
            Failed = a.Failed,
            Pending = a.Pending,
            Delivering = a.Delivering,
            DeliveryRate = resolved > 0 ? (double)a.Delivered / resolved : 0d,
            P95DeliveryMs = a.P95DeliveryMs,
            StatusCodes = statuses.Select(s => new WebhookStatusCodeRow
            {
                Bucket = s.Bucket,
                Count = s.Count,
            }).ToArray(),
            Hosts = hosts.Select(h => new WebhookHostRow
            {
                Host = h.Host,
                Total = h.Total,
                Delivered = h.Delivered,
                Failed = h.Failed,
                DeliveryRate = (h.Delivered + h.Failed) > 0
                    ? (double)h.Delivered / (h.Delivered + h.Failed)
                    : 0d,
            }).ToArray(),
        };
    }

    public async Task<IReadOnlyList<WebhookDeliveryTimeseriesBucket>> GetTimeseriesAsync(
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
                COUNT(*) FILTER (WHERE "Status" = 'Delivered')::bigint                 AS "Delivered",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::bigint                    AS "Failed",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (
                    ORDER BY EXTRACT(EPOCH FROM ("DeliveredAt" - "CreatedAt")) * 1000.0)
                    FILTER (WHERE "Status" = 'Delivered' AND "DeliveredAt" IS NOT NULL), 0)::float8 AS "P95DeliveryMs"
            FROM aihub.webhook_deliveries
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY date_trunc('{{truncUnit}}', "CreatedAt")
            ORDER BY date_trunc('{{truncUnit}}', "CreatedAt") ASC
            """;

        var rows = await db.Database.SqlQueryRaw<BucketRaw>(sql, projectId, from, to).ToListAsync(ct);

        return rows.Select(r => new WebhookDeliveryTimeseriesBucket
        {
            Bucket = r.Bucket,
            Created = r.Created,
            Delivered = r.Delivered,
            Failed = r.Failed,
            P95DeliveryMs = r.P95DeliveryMs,
        }).ToArray();
    }

    private sealed class OverviewAggRaw
    {
        public long TotalDeliveries { get; set; }
        public long Delivered { get; set; }
        public long Failed { get; set; }
        public long Pending { get; set; }
        public long Delivering { get; set; }
        public double P95DeliveryMs { get; set; }
    }

    private sealed class StatusRowRaw
    {
        public string Bucket { get; set; } = "";
        public long Count { get; set; }
    }

    private sealed class HostRowRaw
    {
        public string Host { get; set; } = "";
        public long Total { get; set; }
        public long Delivered { get; set; }
        public long Failed { get; set; }
    }

    private sealed class BucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Created { get; set; }
        public long Delivered { get; set; }
        public long Failed { get; set; }
        public double P95DeliveryMs { get; set; }
    }
}
