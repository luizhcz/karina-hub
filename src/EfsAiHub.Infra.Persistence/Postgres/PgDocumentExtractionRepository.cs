using EfsAiHub.Core.Agents.DocumentIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Repositório para jobs, eventos e cache de extração de documentos.
/// Usa raw NpgsqlDataSource do pool "general" (mesmo padrão de PgExecutionAnalyticsRepository).
///
/// <c>UpsertJobAsync</c> faz INSERT ... ON CONFLICT (id) DO UPDATE em uma única
/// round-trip — sem padrão "try-insert-catch-try-update" que mascarava
/// connection errors. Atomicidade do PG garante semântica.
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

    public async Task UpsertJobAsync(ExtractionJob job, CancellationToken ct)
    {
        // INSERT ... ON CONFLICT (id) DO UPDATE: o caller mantém o ExtractionJob
        // mutável e re-chama UpsertJobAsync a cada transição de status. As
        // colunas imutáveis (conversation_id, user_id, source_type, content_sha256,
        // model, features_hash, created_at) só são gravadas na primeira inserção;
        // em re-chamada, ON CONFLICT preserva valores existentes pra essas e
        // atualiza só as mutáveis.
        const string sql = """
            INSERT INTO aihub.document_extraction_jobs (
                id, conversation_id, user_id,
                source_type, source_ref, content_sha256, model, features_hash,
                status, operation_id, result_ref, page_count, cost_usd,
                error_code, error_message,
                created_at, started_at, finished_at, duration_ms
            )
            VALUES (
                @id, @convId, @userId,
                @srcType, @srcRef, @sha256, @model, @featHash,
                @status, @opId, @resultRef, @pageCount, @costUsd,
                @errCode, @errMsg,
                @createdAt, @startedAt, @finishedAt, @durationMs
            )
            ON CONFLICT (id) DO UPDATE SET
                source_ref    = EXCLUDED.source_ref,
                status        = EXCLUDED.status,
                operation_id  = EXCLUDED.operation_id,
                result_ref    = EXCLUDED.result_ref,
                page_count    = EXCLUDED.page_count,
                cost_usd      = EXCLUDED.cost_usd,
                error_code    = EXCLUDED.error_code,
                error_message = EXCLUDED.error_message,
                started_at    = EXCLUDED.started_at,
                finished_at   = EXCLUDED.finished_at,
                duration_ms   = EXCLUDED.duration_ms
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
        cmd.Parameters.Add(new NpgsqlParameter("opId", NpgsqlDbType.Text) { Value = (object?)job.OperationId ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("resultRef", NpgsqlDbType.Text) { Value = (object?)job.ResultRef ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("pageCount", NpgsqlDbType.Integer) { Value = (object?)job.PageCount ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("costUsd", NpgsqlDbType.Numeric) { Value = (object?)job.CostUsd ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("errCode", NpgsqlDbType.Text) { Value = (object?)job.ErrorCode ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("errMsg", NpgsqlDbType.Text) { Value = (object?)job.ErrorMessage ?? DBNull.Value });
        cmd.Parameters.AddWithValue("createdAt", job.CreatedAt);
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

    public async Task InsertEventsBatchAsync(IReadOnlyList<ExtractionEvent> events, CancellationToken ct)
    {
        if (events is null || events.Count == 0) return;

        // unnest(...) com arrays paralelos: 1 round-trip ao invés de N. Ordem é
        // preservada implicitamente — Postgres atribui occurred_at=now() na ordem
        // de processamento, e como o unnest mantém ordem do array, audit fica
        // consistente. Single statement = single autocommit = atomic.
        // Detail é serializado pro `text[]` e cast pra jsonb dentro do SELECT.
        const string sql = """
            INSERT INTO aihub.document_extraction_events (job_id, event_type, detail)
            SELECT t.job_id, t.event_type,
                   CASE WHEN t.detail IS NULL THEN NULL ELSE t.detail::jsonb END
            FROM unnest(@jobIds::uuid[], @types::text[], @details::text[])
                 AS t(job_id, event_type, detail)
            """;

        var jobIds = new Guid[events.Count];
        var types = new string[events.Count];
        var details = new string?[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            jobIds[i] = events[i].JobId;
            types[i] = events[i].EventType;
            details[i] = events[i].Detail;
        }

        try
        {
            await using var conn = await _ds.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add(new NpgsqlParameter("jobIds", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = jobIds });
            cmd.Parameters.Add(new NpgsqlParameter("types", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = types });
            // Npgsql traduz C# null pra SQL NULL em arrays text[]/jsonb[] automaticamente
            // quando o elemento é string? (não usar DBNull em mid-array, quebra inferência).
            cmd.Parameters.Add(new NpgsqlParameter("details", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = details });
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[DocExtraction] Falha ao inserir batch de {Count} eventos. Eventos perdidos.", events.Count);
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
