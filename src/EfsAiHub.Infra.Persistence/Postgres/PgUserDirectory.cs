using EfsAiHub.Core.Abstractions.Users;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Diretório de usuários sobre Postgres. Usa raw Npgsql pra evitar dependência
/// de EF query filter por tenant — admin lista/edita explicitando o tenant
/// e o middleware de provisioning lookup por (ExternalUserId, TenantId).
/// Upsert é idempotente via UNIQUE constraint UQ_users_ExternalUserId_TenantId.
/// IsAdmin/Permissions não são persistidos — vêm do header em cada request.
/// </summary>
public sealed class PgUserDirectory : IUserDirectory
{
    private readonly NpgsqlDataSource _dataSource;

    public PgUserDirectory(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<UpsertResult> UpsertAsync(
        string externalUserId,
        string userType,
        string tenantId,
        string? displayName,
        CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        // DisplayName só é sobrescrito quando o caller passou um valor novo —
        // null preserva o que admin possa ter editado via UI.
        // xmax = 0 no Postgres RETURNING quando a linha foi recém-inserida; >0
        // quando foi UPDATE — usamos pra distinguir provisão inicial vs reuso.
        cmd.CommandText = """
            INSERT INTO aihub.users
                ("ExternalUserId", "UserType", "TenantId", "DisplayName", "CreatedAt", "LastSeenAt")
            VALUES (@externalUserId, @userType, @tenantId, @displayName, NOW(), NOW())
            ON CONFLICT ("ExternalUserId", "TenantId") DO UPDATE
                SET "UserType"    = EXCLUDED."UserType",
                    "DisplayName" = COALESCE(EXCLUDED."DisplayName", aihub.users."DisplayName"),
                    "LastSeenAt"  = NOW()
            RETURNING "Id", "ExternalUserId", "UserType", "TenantId",
                      "DisplayName", "CreatedAt", "LastSeenAt",
                      (xmax = 0) AS inserted
            """;
        cmd.Parameters.AddWithValue("externalUserId", externalUserId);
        cmd.Parameters.AddWithValue("userType", userType);
        cmd.Parameters.AddWithValue("tenantId", tenantId);
        cmd.Parameters.AddWithValue("displayName", (object?)displayName ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var user = MapUser(reader);
        var inserted = reader.GetBoolean(7);
        return new UpsertResult(user, inserted);
    }

    public async Task<(IReadOnlyList<User> Items, int Total)> ListAsync(
        string tenantId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 25;
        if (pageSize > 200) pageSize = 200;

        var hasSearch = !string.IsNullOrWhiteSpace(search);
        var pattern = hasSearch ? $"%{search!.Trim()}%" : null;

        await using var conn = await _dataSource.OpenConnectionAsync(ct);

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = hasSearch
            ? """
              SELECT COUNT(*)
              FROM aihub.users
              WHERE "TenantId" = @tenantId
                AND ("ExternalUserId" ILIKE @pattern OR "DisplayName" ILIKE @pattern)
              """
            : """
              SELECT COUNT(*) FROM aihub.users WHERE "TenantId" = @tenantId
              """;
        countCmd.Parameters.AddWithValue("tenantId", tenantId);
        if (hasSearch) countCmd.Parameters.AddWithValue("pattern", pattern!);
        var total = Convert.ToInt32(await countCmd.ExecuteScalarAsync(ct));

        await using var listCmd = conn.CreateCommand();
        listCmd.CommandText = hasSearch
            ? """
              SELECT "Id", "ExternalUserId", "UserType", "TenantId",
                     "DisplayName", "CreatedAt", "LastSeenAt"
              FROM aihub.users
              WHERE "TenantId" = @tenantId
                AND ("ExternalUserId" ILIKE @pattern OR "DisplayName" ILIKE @pattern)
              ORDER BY "LastSeenAt" DESC, "Id" ASC
              LIMIT @limit OFFSET @offset
              """
            : """
              SELECT "Id", "ExternalUserId", "UserType", "TenantId",
                     "DisplayName", "CreatedAt", "LastSeenAt"
              FROM aihub.users
              WHERE "TenantId" = @tenantId
              ORDER BY "LastSeenAt" DESC, "Id" ASC
              LIMIT @limit OFFSET @offset
              """;
        listCmd.Parameters.AddWithValue("tenantId", tenantId);
        if (hasSearch) listCmd.Parameters.AddWithValue("pattern", pattern!);
        listCmd.Parameters.AddWithValue("limit", pageSize);
        listCmd.Parameters.AddWithValue("offset", (page - 1) * pageSize);

        var items = new List<User>();
        await using var reader = await listCmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            items.Add(MapUser(reader));
        return (items, total);
    }

    public async Task<User?> GetByExternalIdAsync(string externalUserId, string tenantId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "Id", "ExternalUserId", "UserType", "TenantId",
                   "DisplayName", "CreatedAt", "LastSeenAt"
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
                   "DisplayName", "CreatedAt", "LastSeenAt"
            FROM aihub.users
            WHERE "Id" = @id
            """;
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? MapUser(reader) : null;
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
        CreatedAt      = reader.GetDateTime(5),
        LastSeenAt     = reader.GetDateTime(6),
        IsAdmin        = false,
        Permissions    = Array.Empty<string>(),
    };
}
