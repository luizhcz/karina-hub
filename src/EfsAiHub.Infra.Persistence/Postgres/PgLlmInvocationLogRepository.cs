using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.Capture;
using EfsAiHub.Core.Agents.Composition;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Raw Npgsql porque é hot path (batch INSERT por turno + query interativa
/// com vários filtros opcionais). EF Core entrega menos pra mais código
/// boilerplate aqui.
/// </summary>
public sealed class PgLlmInvocationLogRepository : ILlmInvocationLogRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgLlmInvocationLogRepository> _logger;

    public PgLlmInvocationLogRepository(
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<PgLlmInvocationLogRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task InsertBatchAsync(IReadOnlyList<LlmInvocationLogEntry> entries, CancellationToken ct = default)
    {
        if (entries.Count == 0) return;

        const string sql = @"
INSERT INTO aihub.llm_invocation_log
    (""TurnId"", ""AttemptIndex"", ""ExecutionId"", ""WorkflowId"", ""AgentId"",
     ""AgentVersionId"", ""StepIndex"", ""ProjectId"", ""Provider"", ""ProviderResolved"",
     ""Model"", ""Intent"", ""RequestPayload"", ""ResponsePayload"", ""Composition"",
     ""ChatOptionsSnapshot"", ""Status"", ""ErrorMessage"", ""DurationMs"",
     ""InputTokens"", ""OutputTokens"", ""CachedTokens"",
     ""RequestSizeBytes"", ""ResponseSizeBytes"", ""Truncated"", ""CreatedAt"")
VALUES (@turnId, @attempt, @execId, @wfId, @agentId,
        @agentVerId, @stepIdx, @projectId, @provider, @providerResolved,
        @model, @intent, @reqPayload::jsonb, @respPayload::jsonb, @composition::jsonb,
        @chatOpts::jsonb, @status, @err, @duration,
        @inputTokens, @outputTokens, @cachedTokens,
        @reqSize, @respSize, @truncated, @createdAt);";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        foreach (var e in entries)
        {
            await using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("turnId", e.TurnId);
            cmd.Parameters.AddWithValue("attempt", e.AttemptIndex);
            cmd.Parameters.Add(NullableStr("execId", e.ExecutionId));
            cmd.Parameters.Add(NullableStr("wfId", e.WorkflowId));
            cmd.Parameters.AddWithValue("agentId", e.AgentId);
            cmd.Parameters.Add(NullableStr("agentVerId", e.AgentVersionId));
            cmd.Parameters.Add(NullableInt("stepIdx", e.StepIndex));
            cmd.Parameters.Add(NullableStr("projectId", e.ProjectId));
            cmd.Parameters.AddWithValue("provider", e.Provider);
            cmd.Parameters.AddWithValue("providerResolved", e.ProviderResolved);
            cmd.Parameters.AddWithValue("model", e.Model);
            cmd.Parameters.Add(NullableStr("intent", e.Intent));
            cmd.Parameters.AddWithValue("reqPayload", e.RequestPayload);
            cmd.Parameters.AddWithValue("respPayload", e.ResponsePayload);
            cmd.Parameters.Add(NullableStr("composition", e.Composition is { Count: > 0 } ? JsonSerializer.Serialize(e.Composition, JsonDefaults.Domain) : null));
            cmd.Parameters.Add(NullableStr("chatOpts", e.ChatOptionsSnapshot));
            cmd.Parameters.AddWithValue("status", e.Status);
            cmd.Parameters.Add(NullableStr("err", e.ErrorMessage));
            cmd.Parameters.AddWithValue("duration", e.DurationMs);
            cmd.Parameters.AddWithValue("inputTokens", e.InputTokens);
            cmd.Parameters.AddWithValue("outputTokens", e.OutputTokens);
            cmd.Parameters.AddWithValue("cachedTokens", e.CachedTokens);
            cmd.Parameters.AddWithValue("reqSize", e.RequestSizeBytes);
            cmd.Parameters.AddWithValue("respSize", e.ResponseSizeBytes);
            cmd.Parameters.AddWithValue("truncated", e.Truncated);
            cmd.Parameters.AddWithValue("createdAt", e.CreatedAt);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        _logger.LogDebug("[LlmInvocationLog] Inserted batch of {Count} entries.", entries.Count);
    }

    public async Task<LlmInvocationLogEntry?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
SELECT ""Id"", ""TurnId"", ""AttemptIndex"", ""ExecutionId"", ""WorkflowId"", ""AgentId"",
       ""AgentVersionId"", ""StepIndex"", ""ProjectId"", ""Provider"", ""ProviderResolved"",
       ""Model"", ""Intent"", ""RequestPayload""::text, ""ResponsePayload""::text,
       ""Composition""::text, ""ChatOptionsSnapshot""::text, ""Status"", ""ErrorMessage"",
       ""DurationMs"", ""InputTokens"", ""OutputTokens"", ""CachedTokens"",
       ""RequestSizeBytes"", ""ResponseSizeBytes"", ""Truncated"", ""CreatedAt""
FROM aihub.llm_invocation_log
WHERE ""Id"" = @id
LIMIT 1;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Hydrate(reader);
    }

    public async Task<LlmInvocationLogEntry?> GetByIdAsync(long id, DateTime createdAt, CancellationToken ct = default)
    {
        // PK é composta (Id, CreatedAt) por causa do particionamento — caller passa
        // createdAt pra que o planner faça partition pruning.
        const string sql = @"
SELECT ""Id"", ""TurnId"", ""AttemptIndex"", ""ExecutionId"", ""WorkflowId"", ""AgentId"",
       ""AgentVersionId"", ""StepIndex"", ""ProjectId"", ""Provider"", ""ProviderResolved"",
       ""Model"", ""Intent"", ""RequestPayload""::text, ""ResponsePayload""::text,
       ""Composition""::text, ""ChatOptionsSnapshot""::text, ""Status"", ""ErrorMessage"",
       ""DurationMs"", ""InputTokens"", ""OutputTokens"", ""CachedTokens"",
       ""RequestSizeBytes"", ""ResponseSizeBytes"", ""Truncated"", ""CreatedAt""
FROM aihub.llm_invocation_log
WHERE ""Id"" = @id AND ""CreatedAt"" = @createdAt
LIMIT 1;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("createdAt", createdAt);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return Hydrate(reader);
    }

    public async Task<IReadOnlyList<LlmInvocationLogEntry>> ListAsync(LlmInvocationLogQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = BuildWhere(query);
        var size = Math.Clamp(query.Size, 1, 200);
        var offset = Math.Max(0, (query.Page - 1) * size);

        var sql = $@"
SELECT ""Id"", ""TurnId"", ""AttemptIndex"", ""ExecutionId"", ""WorkflowId"", ""AgentId"",
       ""AgentVersionId"", ""StepIndex"", ""ProjectId"", ""Provider"", ""ProviderResolved"",
       ""Model"", ""Intent"", ''::text, ''::text, ''::text, ''::text,
       ""Status"", ""ErrorMessage"",
       ""DurationMs"", ""InputTokens"", ""OutputTokens"", ""CachedTokens"",
       ""RequestSizeBytes"", ""ResponseSizeBytes"", ""Truncated"", ""CreatedAt""
FROM aihub.llm_invocation_log
{where}
ORDER BY ""CreatedAt"" DESC
LIMIT {size} OFFSET {offset};";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p);

        var list = new List<LlmInvocationLogEntry>(size);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Hydrate(reader));
        return list;
    }

    public async Task<int> CountAsync(LlmInvocationLogQuery query, CancellationToken ct = default)
    {
        var (where, parameters) = BuildWhere(query);
        var sql = $"SELECT COUNT(*) FROM aihub.llm_invocation_log {where};";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var p in parameters) cmd.Parameters.Add(p);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result ?? 0);
    }

    public async Task<IReadOnlyList<LlmInvocationLogEntry>> RecentForDiffAsync(
        string agentId, string? executionId, DateTime beforeCreatedAt, int limit, CancellationToken ct = default)
    {
        var sql = new StringBuilder(@"
SELECT ""Id"", ""TurnId"", ""AttemptIndex"", ""ExecutionId"", ""WorkflowId"", ""AgentId"",
       ""AgentVersionId"", ""StepIndex"", ""ProjectId"", ""Provider"", ""ProviderResolved"",
       ""Model"", ""Intent"", ''::text, ''::text, ''::text, ''::text,
       ""Status"", ""ErrorMessage"",
       ""DurationMs"", ""InputTokens"", ""OutputTokens"", ""CachedTokens"",
       ""RequestSizeBytes"", ""ResponseSizeBytes"", ""Truncated"", ""CreatedAt""
FROM aihub.llm_invocation_log
WHERE ""AgentId"" = @agentId AND ""CreatedAt"" < @before");

        if (!string.IsNullOrEmpty(executionId))
            sql.Append(" AND \"ExecutionId\" = @execId");

        sql.Append(" ORDER BY \"CreatedAt\" DESC LIMIT @lim;");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql.ToString(), conn);
        cmd.Parameters.AddWithValue("agentId", agentId);
        cmd.Parameters.AddWithValue("before", beforeCreatedAt);
        if (!string.IsNullOrEmpty(executionId)) cmd.Parameters.AddWithValue("execId", executionId);
        cmd.Parameters.AddWithValue("lim", Math.Max(1, limit));

        var list = new List<LlmInvocationLogEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            list.Add(Hydrate(reader));
        return list;
    }

    public async Task<IReadOnlyList<string>> DropOldPartitionsAsync(int retentionDays, CancellationToken ct = default)
    {
        // Lista partições, identifica as que estão inteiramente fora da janela
        // de retenção (limite superior <= cutoff) e dropa via DETACH + DROP.
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays).Date;
        var dropped = new List<string>();

        const string listSql = @"
SELECT inhrelid::regclass::text AS partition_name,
       pg_get_expr(relpartbound, inhrelid)  AS bound_expr
FROM pg_inherits
JOIN pg_class ON pg_class.oid = pg_inherits.inhrelid
WHERE inhparent = 'aihub.llm_invocation_log'::regclass;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        var partitions = new List<(string Name, string BoundExpr)>();
        await using (var listCmd = new NpgsqlCommand(listSql, conn))
        await using (var listReader = await listCmd.ExecuteReaderAsync(ct))
        {
            while (await listReader.ReadAsync(ct))
                partitions.Add((listReader.GetString(0), listReader.GetString(1)));
        }

        foreach (var (name, boundExpr) in partitions)
        {
            // boundExpr ex: "FOR VALUES FROM ('2026-04-01 00:00:00-03') TO ('2026-05-01 00:00:00-03')"
            var upper = ExtractUpperBound(boundExpr);
            if (upper is null || upper.Value > cutoff) continue;

            try
            {
                await using var detach = new NpgsqlCommand(
                    $"ALTER TABLE aihub.llm_invocation_log DETACH PARTITION {name};", conn);
                await detach.ExecuteNonQueryAsync(ct);

                await using var drop = new NpgsqlCommand($"DROP TABLE IF EXISTS {name};", conn);
                await drop.ExecuteNonQueryAsync(ct);
                dropped.Add(name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[LlmInvocationLog] Falha ao dropar partição {Name} (upper={Upper}, cutoff={Cutoff}).",
                    name, upper, cutoff);
            }
        }

        return dropped;
    }

    public async Task EnsureFuturePartitionAsync(int monthsAhead, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var target = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(monthsAhead);
        var next = target.AddMonths(1);
        var name = $"llm_invocation_log_{target:yyyy_MM}";

        var sql = $@"
CREATE TABLE IF NOT EXISTS aihub.{name}
    PARTITION OF aihub.llm_invocation_log
    FOR VALUES FROM ('{target:yyyy-MM-dd}') TO ('{next:yyyy-MM-dd}');";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static LlmInvocationLogEntry Hydrate(NpgsqlDataReader r)
    {
        // Quando o caller chamou ListAsync, payloads vêm como '' (placeholder).
        // GetByIdAsync devolve os 4 jsonb completos.
        return new LlmInvocationLogEntry(
            Id: r.GetInt64(0),
            TurnId: r.GetGuid(1),
            AttemptIndex: r.GetInt16(2),
            ExecutionId: r.IsDBNull(3) ? null : r.GetString(3),
            WorkflowId: r.IsDBNull(4) ? null : r.GetString(4),
            AgentId: r.GetString(5),
            AgentVersionId: r.IsDBNull(6) ? null : r.GetString(6),
            StepIndex: r.IsDBNull(7) ? null : r.GetInt32(7),
            ProjectId: r.IsDBNull(8) ? null : r.GetString(8),
            Provider: r.GetString(9),
            ProviderResolved: r.GetString(10),
            Model: r.GetString(11),
            Intent: r.IsDBNull(12) ? null : r.GetString(12),
            RequestPayload: r.IsDBNull(13) ? string.Empty : r.GetString(13),
            ResponsePayload: r.IsDBNull(14) ? string.Empty : r.GetString(14),
            Composition: ReadComposition(r, 15),
            ChatOptionsSnapshot: r.IsDBNull(16) ? null : r.GetString(16),
            Status: r.GetString(17),
            ErrorMessage: r.IsDBNull(18) ? null : r.GetString(18),
            DurationMs: r.GetDouble(19),
            InputTokens: r.GetInt32(20),
            OutputTokens: r.GetInt32(21),
            CachedTokens: r.GetInt32(22),
            RequestSizeBytes: r.GetInt32(23),
            ResponseSizeBytes: r.GetInt32(24),
            Truncated: r.GetBoolean(25),
            CreatedAt: r.GetDateTime(26));
    }

    private static IReadOnlyList<PromptSection>? ReadComposition(NpgsqlDataReader r, int ordinal)
    {
        if (r.IsDBNull(ordinal)) return null;
        var raw = r.GetString(ordinal);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<PromptSection>>(raw);
        }
        catch
        {
            return null;
        }
    }

    private static (string Where, List<NpgsqlParameter> Params) BuildWhere(LlmInvocationLogQuery q)
    {
        var clauses = new List<string>();
        var ps = new List<NpgsqlParameter>();

        if (!string.IsNullOrEmpty(q.AgentId)) { clauses.Add("\"AgentId\" = @agentId"); ps.Add(new("agentId", q.AgentId)); }
        if (!string.IsNullOrEmpty(q.ProjectId)) { clauses.Add("\"ProjectId\" = @projectId"); ps.Add(new("projectId", q.ProjectId)); }
        if (!string.IsNullOrEmpty(q.Intent)) { clauses.Add("\"Intent\" = @intent"); ps.Add(new("intent", q.Intent)); }
        if (!string.IsNullOrEmpty(q.Status)) { clauses.Add("\"Status\" = @status"); ps.Add(new("status", q.Status)); }
        if (!string.IsNullOrEmpty(q.ExecutionId)) { clauses.Add("\"ExecutionId\" = @execId"); ps.Add(new("execId", q.ExecutionId)); }
        if (q.From is { } from) { clauses.Add("\"CreatedAt\" >= @from"); ps.Add(new("from", from)); }
        if (q.To is { } to) { clauses.Add("\"CreatedAt\" <= @to"); ps.Add(new("to", to)); }
        if (q.MinDurationMs is { } min) { clauses.Add("\"DurationMs\" >= @minDur"); ps.Add(new("minDur", min)); }

        var where = clauses.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", clauses);
        return (where, ps);
    }

    private static DateTime? ExtractUpperBound(string boundExpr)
    {
        // Expressão tipo: "FOR VALUES FROM ('2026-04-01 00:00:00-03') TO ('2026-05-01 00:00:00-03')"
        var toIdx = boundExpr.IndexOf("TO (", StringComparison.OrdinalIgnoreCase);
        if (toIdx < 0) return null;
        var start = boundExpr.IndexOf('\'', toIdx);
        if (start < 0) return null;
        var end = boundExpr.IndexOf('\'', start + 1);
        if (end < 0) return null;
        var raw = boundExpr.Substring(start + 1, end - start - 1);
        if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
            return dt;
        return null;
    }

    private static NpgsqlParameter NullableStr(string name, string? value)
    {
        var p = new NpgsqlParameter { ParameterName = name };
        p.Value = value is null ? DBNull.Value : value;
        return p;
    }

    private static NpgsqlParameter NullableInt(string name, int? value)
    {
        var p = new NpgsqlParameter { ParameterName = name };
        p.Value = value.HasValue ? value.Value : DBNull.Value;
        return p;
    }
}
