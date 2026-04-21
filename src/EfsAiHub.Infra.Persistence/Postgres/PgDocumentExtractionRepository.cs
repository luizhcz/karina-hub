using System.Text.Json;
using EfsAiHub.Core.Agents.DocumentIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Repositório para jobs, eventos e cache de extração de documentos.
/// Usa raw NpgsqlDataSource do pool "general" (mesmo padrão de PgExecutionAnalyticsRepository).
/// </summary>
public class PgDocumentExtractionRepository : IDocumentExtractionRepository
{
    private readonly NpgsqlDataSource _ds;
    private readonly ILogger<PgDocumentExtractionRepository> _logger;

    public PgDocumentExtractionRepository(
        [FromKeyedServices("general")] NpgsqlDataSource ds,
        ILogger<PgDocumentExtractionRepository> logger)
    {
        _ds = ds;
        _logger = logger;
    }

    public async Task InsertJobAsync(ExtractionJob job, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO aihub.document_extraction_jobs
                (id, conversation_id, user_id, source_type, source_ref, content_sha256,
                 model, features_hash, status, created_at)
            VALUES
                (@id, @convId, @userId, @srcType, @srcRef, @sha256,
                 @model, @featHash, @status, @createdAt)
            """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", job.Id);
        cmd.Parameters.AddWithValue("convId", job.ConversationId);
        cmd.Parameters.AddWithValue("userId", job.UserId);
        cmd.Parameters.AddWithValue("srcType", job.SourceType);
        cmd.Parameters.Add(new NpgsqlParameter("srcRef", NpgsqlDbType.Text) { Value = (object?)job.SourceRef ?? DBNull.Value });
        cmd.Parameters.AddWithValue("sha256", job.ContentSha256);
        cmd.Parameters.AddWithValue("model", job.Model);
        cmd.Parameters.Add(new NpgsqlParameter("featHash", NpgsqlDbType.Text) { Value = (object?)job.FeaturesHash ?? DBNull.Value });
        cmd.Parameters.AddWithValue("status", job.Status);
        cmd.Parameters.AddWithValue("createdAt", job.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobAsync(ExtractionJob job, CancellationToken ct)
    {
        const string sql = """
            UPDATE aihub.document_extraction_jobs SET
                status = @status, operation_id = @opId, result_ref = @resultRef,
                page_count = @pageCount, cost_usd = @costUsd,
                error_code = @errCode, error_message = @errMsg,
                started_at = @startedAt, finished_at = @finishedAt, duration_ms = @durationMs
            WHERE id = @id
            """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", job.Id);
        cmd.Parameters.AddWithValue("status", job.Status);
        cmd.Parameters.Add(new NpgsqlParameter("opId", NpgsqlDbType.Text) { Value = (object?)job.OperationId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("resultRef", NpgsqlDbType.Text) { Value = (object?)job.ResultRef ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("pageCount", NpgsqlDbType.Integer) { Value = (object?)job.PageCount ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("costUsd", NpgsqlDbType.Numeric) { Value = (object?)job.CostUsd ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("errCode", NpgsqlDbType.Text) { Value = (object?)job.ErrorCode ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("errMsg", NpgsqlDbType.Text) { Value = (object?)job.ErrorMessage ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("startedAt", NpgsqlDbType.TimestampTz) { Value = (object?)job.StartedAt ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("finishedAt", NpgsqlDbType.TimestampTz) { Value = (object?)job.FinishedAt ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("durationMs", NpgsqlDbType.Integer) { Value = (object?)job.DurationMs ?? DBNull.Value });
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task InsertEventAsync(ExtractionEvent evt, CancellationToken ct)
    {
        try
        {
            const string sql = """
                INSERT INTO aihub.document_extraction_events (job_id, event_type, detail)
                VALUES (@jobId, @eventType, @detail::jsonb)
                """;

            await using var conn = await _ds.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("jobId", evt.JobId);
            cmd.Parameters.AddWithValue("eventType", evt.EventType);
            cmd.Parameters.Add(new NpgsqlParameter("detail", NpgsqlDbType.Text) { Value = (object?)evt.Detail ?? DBNull.Value });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocExtraction] Falha ao inserir evento '{EventType}' para job '{JobId}'.", evt.EventType, evt.JobId);
        }
    }

    public async Task<ExtractionCacheEntry?> LookupCacheAsync(string sha256, string model, string featuresHash, CancellationToken ct)
    {
        const string sql = """
            SELECT result_ref, page_count, expires_at
            FROM aihub.document_extraction_cache
            WHERE content_sha256 = @sha256 AND model = @model AND features_hash = @featHash
              AND expires_at > now()
            """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sha256", sha256);
        cmd.Parameters.AddWithValue("model", model);
        cmd.Parameters.AddWithValue("featHash", featuresHash);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new ExtractionCacheEntry(
            sha256, model, featuresHash,
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetDateTime(2));
    }

    public async Task UpsertCacheAsync(ExtractionCacheEntry entry, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO aihub.document_extraction_cache
                (content_sha256, model, features_hash, result_ref, page_count, expires_at)
            VALUES (@sha256, @model, @featHash, @resultRef, @pageCount, @expiresAt)
            ON CONFLICT (content_sha256, model, features_hash) DO UPDATE SET
                result_ref = EXCLUDED.result_ref,
                page_count = EXCLUDED.page_count,
                expires_at = EXCLUDED.expires_at
            """;

        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("sha256", entry.ContentSha256);
        cmd.Parameters.AddWithValue("model", entry.Model);
        cmd.Parameters.AddWithValue("featHash", entry.FeaturesHash);
        cmd.Parameters.AddWithValue("resultRef", entry.ResultRef);
        cmd.Parameters.AddWithValue("pageCount", entry.PageCount);
        cmd.Parameters.AddWithValue("expiresAt", entry.ExpiresAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
