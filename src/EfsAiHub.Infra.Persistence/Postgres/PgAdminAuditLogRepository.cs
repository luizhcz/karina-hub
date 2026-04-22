using System.Text;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persistência via raw Npgsql no pool "general" (writes + reads interativos).
/// Escolha por SQL direto: INSERT é hot path (chamado em todo CRUD admin) e o caller
/// descarta PayloadBefore/After como JsonDocument serializado — driver bind nativo
/// de JsonDocument em Npgsql cria overhead desnecessário para payloads opacos.
///
/// Falhas de INSERT são logadas como warning e engolidas — auditoria é secundária
/// ao request path (melhor perder uma linha de log do que falhar o CRUD do usuário).
/// </summary>
public sealed class PgAdminAuditLogRepository : IAdminAuditLogger
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgAdminAuditLogRepository> _logger;

    public PgAdminAuditLogRepository(
        [FromKeyedServices("general")] NpgsqlDataSource dataSource,
        ILogger<PgAdminAuditLogRepository> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task RecordAsync(AdminAuditEntry entry, CancellationToken ct = default)
    {
        const string sql = @"
INSERT INTO aihub.admin_audit_log
    (""TenantId"", ""ProjectId"", ""ActorUserId"", ""ActorUserType"", ""Action"",
     ""ResourceType"", ""ResourceId"", ""PayloadBefore"", ""PayloadAfter"", ""Timestamp"")
VALUES (@tenantId, @projectId, @actorUserId, @actorUserType, @action,
        @resourceType, @resourceId, @before::jsonb, @after::jsonb, @timestamp);";

        try
        {
            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.Add(Nullable("tenantId", entry.TenantId));
            cmd.Parameters.Add(Nullable("projectId", entry.ProjectId));
            cmd.Parameters.AddWithValue("actorUserId", entry.ActorUserId);
            cmd.Parameters.Add(Nullable("actorUserType", entry.ActorUserType));
            cmd.Parameters.AddWithValue("action", entry.Action);
            cmd.Parameters.AddWithValue("resourceType", entry.ResourceType);
            cmd.Parameters.AddWithValue("resourceId", entry.ResourceId);
            cmd.Parameters.Add(Nullable("before", Serialize(entry.PayloadBefore)));
            cmd.Parameters.Add(Nullable("after", Serialize(entry.PayloadAfter)));
            cmd.Parameters.AddWithValue("timestamp", entry.Timestamp == default
                ? DateTime.UtcNow
                : entry.Timestamp);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Não quebra o CRUD caso o INSERT de auditoria falhe (disco cheio, tabela lockada, etc).
            _logger.LogWarning(ex,
                "[AdminAudit] INSERT falhou. actor={Actor} resource={ResourceType}/{ResourceId} action={Action}",
                entry.ActorUserId, entry.ResourceType, entry.ResourceId, entry.Action);
        }
    }

    public async Task<IReadOnlyList<AdminAuditEntry>> QueryAsync(AdminAuditQuery query, CancellationToken ct = default)
    {
        var (whereClause, parameters) = BuildWhere(query);

        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var page = Math.Max(1, query.Page);
        var offset = (page - 1) * pageSize;

        var sql = $@"
SELECT ""Id"", ""TenantId"", ""ProjectId"", ""ActorUserId"", ""ActorUserType"",
       ""Action"", ""ResourceType"", ""ResourceId"",
       ""PayloadBefore""::text, ""PayloadAfter""::text, ""Timestamp""
FROM aihub.admin_audit_log
{whereClause}
ORDER BY ""Timestamp"" DESC, ""Id"" DESC
LIMIT {pageSize} OFFSET {offset};";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value, type) in parameters)
            cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

        var results = new List<AdminAuditEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(Map(reader));

        return results;
    }

    public async Task<int> CountAsync(AdminAuditQuery query, CancellationToken ct = default)
    {
        var (whereClause, parameters) = BuildWhere(query);
        var sql = $"SELECT COUNT(*) FROM aihub.admin_audit_log {whereClause};";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value, type) in parameters)
            cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result ?? 0);
    }

    private static (string where, List<(string Name, object? Value, NpgsqlDbType Type)> Params)
        BuildWhere(AdminAuditQuery q)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object?, NpgsqlDbType)>();

        void Add(string column, string param, object? value, NpgsqlDbType type)
        {
            if (value is null) return;
            clauses.Add($"\"{column}\" = @{param}");
            parameters.Add((param, value, type));
        }

        Add("TenantId", "tenantId", q.TenantId, NpgsqlDbType.Varchar);
        Add("ProjectId", "projectId", q.ProjectId, NpgsqlDbType.Varchar);
        Add("ResourceType", "resourceType", q.ResourceType, NpgsqlDbType.Varchar);
        Add("ResourceId", "resourceId", q.ResourceId, NpgsqlDbType.Varchar);
        Add("ActorUserId", "actorUserId", q.ActorUserId, NpgsqlDbType.Varchar);
        Add("Action", "action", q.Action, NpgsqlDbType.Varchar);

        if (q.From.HasValue)
        {
            clauses.Add("\"Timestamp\" >= @from");
            parameters.Add(("from", q.From.Value, NpgsqlDbType.TimestampTz));
        }
        if (q.To.HasValue)
        {
            clauses.Add("\"Timestamp\" <= @to");
            parameters.Add(("to", q.To.Value, NpgsqlDbType.TimestampTz));
        }

        var where = clauses.Count == 0 ? "" : "WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    private static string? Serialize(JsonDocument? doc)
        => doc is null ? null : JsonSerializer.Serialize(doc.RootElement);

    private static NpgsqlParameter Nullable(string name, string? value)
        => new(name, NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value };

    private static AdminAuditEntry Map(NpgsqlDataReader r)
    {
        var beforeJson = r.IsDBNull(8) ? null : r.GetString(8);
        var afterJson = r.IsDBNull(9) ? null : r.GetString(9);

        return new AdminAuditEntry
        {
            Id = r.GetInt64(0),
            TenantId = r.IsDBNull(1) ? null : r.GetString(1),
            ProjectId = r.IsDBNull(2) ? null : r.GetString(2),
            ActorUserId = r.GetString(3),
            ActorUserType = r.IsDBNull(4) ? null : r.GetString(4),
            Action = r.GetString(5),
            ResourceType = r.GetString(6),
            ResourceId = r.GetString(7),
            PayloadBefore = beforeJson is null ? null : JsonDocument.Parse(beforeJson),
            PayloadAfter = afterJson is null ? null : JsonDocument.Parse(afterJson),
            Timestamp = DateTime.SpecifyKind(r.GetDateTime(10), DateTimeKind.Utc),
        };
    }
}
