using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgDocumentIntelligenceUsageQueries : IDocumentIntelligenceUsageQueries
{
    private readonly NpgsqlDataSource _dataSource;

    public PgDocumentIntelligenceUsageQueries([FromKeyedServices("reporting")] NpgsqlDataSource dataSource)
        => _dataSource = dataSource;

    public async Task<DocumentIntelligenceUsageSummary> GetSummaryAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                COUNT(*)::bigint AS total_jobs,
                COUNT(*) FILTER (WHERE status = 'succeeded')::bigint AS succeeded_jobs,
                COUNT(*) FILTER (WHERE status = 'cached')::bigint AS cached_jobs,
                COUNT(*) FILTER (WHERE status = 'failed')::bigint AS failed_jobs,
                COALESCE(SUM(page_count), 0)::bigint AS total_pages,
                COALESCE(SUM(cost_usd), 0)::numeric(20,6) AS total_cost_usd
            FROM aihub.document_extraction_jobs
            WHERE created_at >= @from AND created_at < @to;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new DocumentIntelligenceUsageSummary(0, 0, 0, 0, 0, 0m);

        return new DocumentIntelligenceUsageSummary(
            TotalJobs: reader.GetInt64(0),
            SucceededJobs: reader.GetInt64(1),
            CachedJobs: reader.GetInt64(2),
            FailedJobs: reader.GetInt64(3),
            TotalPages: reader.GetInt64(4),
            TotalCostUsd: reader.GetDecimal(5));
    }

    public async Task<IReadOnlyList<DocumentIntelligenceUsageByDay>> GetByDayAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                (date_trunc('day', created_at))::date AS day,
                COUNT(*)::bigint AS job_count,
                COALESCE(SUM(page_count), 0)::bigint AS pages,
                COALESCE(SUM(cost_usd), 0)::numeric(20,6) AS cost_usd
            FROM aihub.document_extraction_jobs
            WHERE created_at >= @from AND created_at < @to
              AND status IN ('succeeded', 'cached')
            GROUP BY 1
            ORDER BY 1;";

        var list = new List<DocumentIntelligenceUsageByDay>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentIntelligenceUsageByDay(
                Day: DateOnly.FromDateTime(reader.GetDateTime(0)),
                JobCount: reader.GetInt64(1),
                Pages: reader.GetInt64(2),
                CostUsd: reader.GetDecimal(3)));
        }
        return list;
    }

    public async Task<IReadOnlyList<DocumentIntelligenceUsageByModel>> GetByModelAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                model,
                COUNT(*)::bigint AS job_count,
                COALESCE(SUM(page_count), 0)::bigint AS pages,
                COALESCE(SUM(cost_usd), 0)::numeric(20,6) AS cost_usd
            FROM aihub.document_extraction_jobs
            WHERE created_at >= @from AND created_at < @to
              AND status IN ('succeeded', 'cached')
            GROUP BY model
            ORDER BY cost_usd DESC;";

        var list = new List<DocumentIntelligenceUsageByModel>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentIntelligenceUsageByModel(
                Model: reader.GetString(0),
                JobCount: reader.GetInt64(1),
                Pages: reader.GetInt64(2),
                CostUsd: reader.GetDecimal(3)));
        }
        return list;
    }

    public async Task<IReadOnlyList<DocumentIntelligenceJobSummary>> GetRecentJobsAsync(
        DateTime from, DateTime to, int limit, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, conversation_id, user_id, model, status,
                   page_count, cost_usd, duration_ms, created_at
            FROM aihub.document_extraction_jobs
            WHERE created_at >= @from AND created_at < @to
            ORDER BY created_at DESC
            LIMIT @limit;";

        var list = new List<DocumentIntelligenceJobSummary>();
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.AddWithValue("limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new DocumentIntelligenceJobSummary(
                JobId: reader.GetGuid(0),
                ConversationId: reader.GetString(1),
                UserId: reader.GetString(2),
                Model: reader.GetString(3),
                Status: reader.GetString(4),
                PageCount: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                CostUsd: reader.IsDBNull(6) ? null : reader.GetDecimal(6),
                DurationMs: reader.IsDBNull(7) ? null : reader.GetInt32(7),
                CreatedAt: reader.GetDateTime(8)));
        }
        return list;
    }
}
