using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Analytics de message_feedbacks via SqlQueryRaw. Recent listing faz LEFT
/// JOIN com chat_messages — quando a mensagem foi deletada (TTL de chat
/// pós-cleanup), `MessagePreview` vem null e a UI mostra "(mensagem
/// indisponível)".
/// </summary>
public sealed class PgFeedbackAnalyticsRepository : IFeedbackAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgFeedbackAnalyticsRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<FeedbackOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // COALESCE em SUM porque a query agg SEM GROUP BY devolve uma única
        // linha com SUM=NULL quando 0 rows casam o filtro (projeto sem
        // feedback no período). EF não consegue mapear NULL em `long` não-
        // nullable. COUNT(*) já é safe (sempre 0+).
        var aggSql = """
            SELECT
                COUNT(*)::bigint                                                            AS "Total",
                COALESCE(SUM(CASE WHEN "Sentiment" =  1 THEN 1 ELSE 0 END), 0)::bigint      AS "Positives",
                COALESCE(SUM(CASE WHEN "Sentiment" = -1 THEN 1 ELSE 0 END), 0)::bigint      AS "Negatives",
                COUNT(DISTINCT "MessageId")::int                                            AS "DistinctMessages",
                COUNT(DISTINCT "UserId")::int                                               AS "DistinctUsers",
                COUNT(*) FILTER (WHERE "Comment" IS NOT NULL AND "Comment" <> '')::bigint   AS "WithCommentCount"
            FROM aihub.message_feedbacks
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            """;

        var agg = await db.Database.SqlQueryRaw<OverviewAggRaw>(aggSql, projectId, from, to).ToListAsync(ct);
        var a = agg.FirstOrDefault() ?? new OverviewAggRaw();

        // Top 10 mensagens com mais feedback. MAX() nas colunas não-agrupadas
        // pra que GROUP BY MessageId funcione (cada MessageId tem 1 Content
        // e 1 ConversationId, então MAX devolve o valor único).
        var topSql = """
            SELECT
                f."MessageId"                                                   AS "MessageId",
                COUNT(*)::bigint                                                AS "FeedbackCount",
                SUM(CASE WHEN f."Sentiment" =  1 THEN 1 ELSE 0 END)::bigint     AS "Positives",
                SUM(CASE WHEN f."Sentiment" = -1 THEN 1 ELSE 0 END)::bigint     AS "Negatives",
                MAX(LEFT(m."Content", 200))                                     AS "MessagePreview",
                MAX(f."ConversationId")                                         AS "ConversationId"
            FROM aihub.message_feedbacks f
            LEFT JOIN aihub.chat_messages m ON m."MessageId" = f."MessageId"
            WHERE f."ProjectId" = {0}
              AND f."CreatedAt" BETWEEN {1} AND {2}
            GROUP BY f."MessageId"
            ORDER BY COUNT(*) DESC
            LIMIT 10
            """;

        var top = await db.Database.SqlQueryRaw<TopRowRaw>(topSql, projectId, from, to).ToListAsync(ct);

        var resolved = a.Positives + a.Negatives;
        return new FeedbackOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            Total = a.Total,
            Positives = a.Positives,
            Negatives = a.Negatives,
            SatisfactionRate = resolved > 0 ? (double)a.Positives / resolved : 0d,
            DistinctMessages = a.DistinctMessages,
            DistinctUsers = a.DistinctUsers,
            WithCommentCount = a.WithCommentCount,
            TopMessages = top.Select(t => new FeedbackTopMessageRow
            {
                MessageId = t.MessageId,
                FeedbackCount = t.FeedbackCount,
                Positives = t.Positives,
                Negatives = t.Negatives,
                MessagePreview = t.MessagePreview,
                ConversationId = t.ConversationId,
            }).ToArray(),
        };
    }

    public async Task<IReadOnlyList<FeedbackTimeseriesBucket>> GetTimeseriesAsync(
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
                date_trunc('{{truncUnit}}', "CreatedAt")::timestamptz             AS "Bucket",
                COUNT(*)::bigint                                                  AS "Count",
                SUM(CASE WHEN "Sentiment" =  1 THEN 1 ELSE 0 END)::bigint         AS "Positives",
                SUM(CASE WHEN "Sentiment" = -1 THEN 1 ELSE 0 END)::bigint         AS "Negatives"
            FROM aihub.message_feedbacks
            WHERE "ProjectId" = {0}
              AND "CreatedAt" BETWEEN {1} AND {2}
            GROUP BY date_trunc('{{truncUnit}}', "CreatedAt")
            ORDER BY date_trunc('{{truncUnit}}', "CreatedAt") ASC
            """;

        var rows = await db.Database.SqlQueryRaw<BucketRaw>(sql, projectId, from, to).ToListAsync(ct);

        return rows.Select(r => new FeedbackTimeseriesBucket
        {
            Bucket = r.Bucket,
            Count = r.Count,
            Positives = r.Positives,
            Negatives = r.Negatives,
        }).ToArray();
    }

    public async Task<FeedbackRecentResponse> GetRecentAsync(
        string projectId,
        DateTime from,
        DateTime to,
        int? sentiment,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Sentiment filter aplicado condicionalmente — em vez de passar NULL
        // como parâmetro e fazer "IS NULL OR" no SQL (que confunde planner),
        // monto a string com o WHERE extra. Cada query tem placeholder próprio
        // pro sentiment porque o number of params anteriores difere:
        // countSql usa {3}, itemsSql usa {5}.
        var sentimentClauseCount = sentiment.HasValue ? @" AND f.""Sentiment"" = {3}" : string.Empty;
        var sentimentClauseItems = sentiment.HasValue ? @" AND f.""Sentiment"" = {5}" : string.Empty;
        var offset = Math.Max(0, (page - 1) * pageSize);

        var countSql = $@"
            SELECT COUNT(*)::bigint AS ""Total""
            FROM aihub.message_feedbacks f
            WHERE f.""ProjectId"" = {{0}}
              AND f.""CreatedAt"" BETWEEN {{1}} AND {{2}}
              {sentimentClauseCount}
            ";

        var itemsSql = $@"
            SELECT
                f.""FeedbackId""                                                AS ""FeedbackId"",
                f.""MessageId""                                                 AS ""MessageId"",
                f.""ConversationId""                                            AS ""ConversationId"",
                m.""ExecutionId""                                               AS ""ExecutionId"",
                f.""Sentiment""                                                 AS ""Sentiment"",
                f.""Comment""                                                   AS ""Comment"",
                f.""UserId""                                                    AS ""UserId"",
                LEFT(m.""Content"", 300)                                        AS ""MessagePreview"",
                f.""CreatedAt""                                                 AS ""CreatedAt""
            FROM aihub.message_feedbacks f
            LEFT JOIN aihub.chat_messages m ON m.""MessageId"" = f.""MessageId""
            WHERE f.""ProjectId"" = {{0}}
              AND f.""CreatedAt"" BETWEEN {{1}} AND {{2}}
              {sentimentClauseItems}
            ORDER BY f.""CreatedAt"" DESC
            LIMIT {{3}} OFFSET {{4}}
            ";

        long total;
        List<RecentRowRaw> items;

        if (sentiment.HasValue)
        {
            var totalRows = await db.Database.SqlQueryRaw<CountRaw>(countSql, projectId, from, to, sentiment.Value)
                .ToListAsync(ct);
            total = totalRows.FirstOrDefault()?.Total ?? 0;
            items = await db.Database
                .SqlQueryRaw<RecentRowRaw>(itemsSql, projectId, from, to, pageSize, offset, sentiment.Value)
                .ToListAsync(ct);
        }
        else
        {
            var totalRows = await db.Database.SqlQueryRaw<CountRaw>(countSql, projectId, from, to)
                .ToListAsync(ct);
            total = totalRows.FirstOrDefault()?.Total ?? 0;
            items = await db.Database
                .SqlQueryRaw<RecentRowRaw>(itemsSql, projectId, from, to, pageSize, offset)
                .ToListAsync(ct);
        }

        return new FeedbackRecentResponse
        {
            Items = items.Select(r => new FeedbackRecentRow
            {
                FeedbackId = r.FeedbackId,
                MessageId = r.MessageId,
                ConversationId = r.ConversationId,
                ExecutionId = r.ExecutionId,
                Sentiment = r.Sentiment,
                Comment = r.Comment,
                UserId = r.UserId,
                MessagePreview = r.MessagePreview,
                CreatedAt = r.CreatedAt,
            }).ToArray(),
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    private sealed class OverviewAggRaw
    {
        public long Total { get; set; }
        public long Positives { get; set; }
        public long Negatives { get; set; }
        public int DistinctMessages { get; set; }
        public int DistinctUsers { get; set; }
        public long WithCommentCount { get; set; }
    }

    private sealed class TopRowRaw
    {
        public string MessageId { get; set; } = "";
        public long FeedbackCount { get; set; }
        public long Positives { get; set; }
        public long Negatives { get; set; }
        public string? MessagePreview { get; set; }
        public string? ConversationId { get; set; }
    }

    private sealed class BucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Count { get; set; }
        public long Positives { get; set; }
        public long Negatives { get; set; }
    }

    private sealed class CountRaw
    {
        public long Total { get; set; }
    }

    private sealed class RecentRowRaw
    {
        public string FeedbackId { get; set; } = "";
        public string MessageId { get; set; } = "";
        public string? ConversationId { get; set; }
        public string? ExecutionId { get; set; }
        public int Sentiment { get; set; }
        public string? Comment { get; set; }
        public string? UserId { get; set; }
        public string? MessagePreview { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
