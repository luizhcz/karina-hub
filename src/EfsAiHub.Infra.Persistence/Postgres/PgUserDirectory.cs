using EfsAiHub.Core.Abstractions.Users;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Diretório de usuários sobre Postgres. Usa raw Npgsql pra evitar dependência
/// de EF query filter por tenant — admin lista/edita explicitando o tenant
/// e o middleware de provisioning lookup por (ExternalUserId, TenantId).
/// Upsert é idempotente via UNIQUE constraint UQ_users_ExternalUserId_TenantId.
/// </summary>
public sealed class PgUserDirectory : IUserDirectory
{
    private readonly NpgsqlDataSource _dataSource;

    public PgUserDirectory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<User> UpsertAsync(
        string externalUserId,
        string userType,
        string tenantId,
        string? displayName,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // ON CONFLICT preserva IsAdmin existente (atualizado só via SetAdminAsync).
        // DisplayName só é sobrescrito quando o caller passou um valor novo —
        // null preserva o que admin possa ter editado via UI.
        cmd.CommandText = """
            INSERT INTO aihub.users
                ("ExternalUserId", "UserType", "TenantId", "DisplayName", "CreatedAt", "LastSeenAt")
            VALUES (@externalUserId, @userType, @tenantId, @displayName, NOW(), NOW())
            ON CONFLICT ("ExternalUserId", "TenantId") DO UPDATE
                SET "UserType"    = EXCLUDED."UserType",
                    "DisplayName" = COALESCE(EXCLUDED."DisplayName", aihub.users."DisplayName"),
                    "LastSeenAt"  = NOW()
            RETURNING "Id", "ExternalUserId", "UserType", "TenantId",
                      "DisplayName", "IsAdmin", "CreatedAt", "LastSeenAt"
            """;
        cmd.Parameters.AddWithValue("externalUserId", externalUserId);
        cmd.Parameters.AddWithValue("userType", userType);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("displayName", (object?)displayName ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return MapUser(reader);
    }

    public async Task<User?> GetByExternalIdAsync(string externalUserId, string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "ExternalUserId", "UserType", "TenantId",
                   "DisplayName", "IsAdmin", "CreatedAt", "LastSeenAt"
            FROM aihub.users
            WHERE "ExternalUserId" = @externalUserId AND "TenantId" = @tenantId
            """;
        cmd.Parameters.AddWithValue("externalUserId", externalUserId);
        cmd.Parameters.AddWithValue("tenantId", tenantId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapUser(reader) : null;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "ExternalUserId", "UserType", "TenantId",
                   "DisplayName", "IsAdmin", "CreatedAt", "LastSeenAt"
            FROM aihub.users
            WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapUser(reader) : null;
    }

    public async Task SetAdminAsync(Guid userId, bool isAdmin, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE aihub.users
               SET "IsAdmin" = @isAdmin
             WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("isAdmin", isAdmin);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetDisplayNameAsync(Guid userId, string displayName, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE aihub.users
               SET "DisplayName" = @displayName
             WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", userId);
        cmd.Parameters.AddWithValue("displayName", displayName);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static User MapUser(NpgsqlDataReader reader) => new()
    {
        Id             = reader.GetGuid(0),
        ExternalUserId = reader.GetString(1),
        UserType       = reader.GetString(2),
        TenantId       = reader.GetString(3),
        DisplayName    = reader.IsDBNull(4) ? null : reader.GetString(4),
        IsAdmin        = reader.GetBoolean(5),
        CreatedAt      = reader.GetDateTime(6),
        LastSeenAt     = reader.GetDateTime(7),
    };
}
