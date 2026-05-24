using System.Text.Json;
using EfsAiHub.Core.Abstractions.Persistence;
using EfsAiHub.Core.Agents.Capture;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Singleton row em <c>aihub.llm_capture_config</c> (Id=1 fixo). UPSERT
/// idempotente porque o admin pode "ligar e desligar" várias vezes na
/// mesma sessão e queremos preservar histórico só no <c>UpdatedAt</c>
/// (audit fica no <c>admin_audit_log</c> via export).
/// </summary>
public sealed class PgLlmCaptureConfigRepository : ILlmCaptureConfigRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgLlmCaptureConfigRepository> _logger;

    public PgLlmCaptureConfigRepository(
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<PgLlmCaptureConfigRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<LlmCaptureConfig> GetAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT ""Enabled"", ""ProjectIds"", ""AgentIds"", ""WorkflowIds"",
       ""ExpiresAt"", ""EnabledBy"", ""EnabledAt"", ""UpdatedAt""
FROM aihub.llm_capture_config WHERE ""Id"" = 1;";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            return LlmCaptureConfig.Disabled();

        return new LlmCaptureConfig(
            Enabled: reader.GetBoolean(0),
            ProjectIds: ReadStringArray(reader, 1),
            AgentIds: ReadStringArray(reader, 2),
            WorkflowIds: ReadStringArray(reader, 3),
            ExpiresAt: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            EnabledBy: reader.IsDBNull(5) ? null : reader.GetString(5),
            EnabledAt: reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            UpdatedAt: reader.GetDateTime(7));
    }

    public async Task<LlmCaptureConfig> UpsertAsync(LlmCaptureConfig config, CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO aihub.llm_capture_config
    (""Id"", ""Enabled"", ""ProjectIds"", ""AgentIds"", ""WorkflowIds"",
     ""ExpiresAt"", ""EnabledBy"", ""EnabledAt"", ""UpdatedAt"")
VALUES (1, @enabled, @projects::jsonb, @agents::jsonb, @workflows::jsonb,
        @expiresAt, @enabledBy, @enabledAt, NOW())
ON CONFLICT (""Id"") DO UPDATE
    SET ""Enabled""     = EXCLUDED.""Enabled"",
        ""ProjectIds""  = EXCLUDED.""ProjectIds"",
        ""AgentIds""    = EXCLUDED.""AgentIds"",
        ""WorkflowIds"" = EXCLUDED.""WorkflowIds"",
        ""ExpiresAt""   = EXCLUDED.""ExpiresAt"",
        ""EnabledBy""   = EXCLUDED.""EnabledBy"",
        ""EnabledAt""   = EXCLUDED.""EnabledAt"",
        ""UpdatedAt""   = NOW()
RETURNING ""UpdatedAt"";";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("enabled", config.Enabled);
        cmd.Parameters.Add(JsonbNullable("projects", config.ProjectIds));
        cmd.Parameters.Add(JsonbNullable("agents", config.AgentIds));
        cmd.Parameters.Add(JsonbNullable("workflows", config.WorkflowIds));
        cmd.Parameters.Add(Nullable("expiresAt", config.ExpiresAt, NpgsqlDbType.TimestampTz));
        cmd.Parameters.Add(Nullable("enabledBy", config.EnabledBy));
        cmd.Parameters.Add(Nullable("enabledAt", config.EnabledAt, NpgsqlDbType.TimestampTz));

        var updatedAt = (DateTime)(await cmd.ExecuteScalarAsync(ct) ?? DateTime.UtcNow);

        _logger.LogInformation(
            "[LlmCaptureConfig] Upsert: Enabled={Enabled}, ExpiresAt={ExpiresAt}, EnabledBy={By}",
            config.Enabled, config.ExpiresAt, config.EnabledBy);

        return config with { UpdatedAt = updatedAt };
    }

    private static IReadOnlyList<string>? ReadStringArray(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return null;
        var raw = reader.GetString(ordinal);
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(raw);
            return parsed is { Length: > 0 } ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static NpgsqlParameter JsonbNullable(string name, IReadOnlyList<string>? values)
    {
        var p = new NpgsqlParameter(name, NpgsqlDbType.Jsonb);
        if (values is { Count: > 0 })
            p.Value = JsonSerializer.Serialize(values, JsonDefaults.Domain);
        else
            p.Value = DBNull.Value;
        return p;
    }

    private static NpgsqlParameter Nullable<T>(string name, T? value, NpgsqlDbType? dbType = null) where T : struct
    {
        var p = dbType.HasValue ? new NpgsqlParameter(name, dbType.Value) : new NpgsqlParameter { ParameterName = name };
        p.Value = value.HasValue ? (object)value.Value : DBNull.Value;
        return p;
    }

    private static NpgsqlParameter Nullable(string name, string? value)
    {
        var p = new NpgsqlParameter { ParameterName = name };
        p.Value = value is null ? DBNull.Value : value;
        return p;
    }
}
