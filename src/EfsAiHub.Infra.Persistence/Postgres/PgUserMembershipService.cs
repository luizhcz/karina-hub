using EfsAiHub.Core.Abstractions.Users;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Membership service sobre Postgres com cache em memória de 60s. Cache é
/// por (tenantId, externalUserId) pra autorização hot-path e por (userId)
/// pra UI admin. Invalida explicitamente quando o set muda
/// (<see cref="AssignProjectsAsync"/> ou <see cref="InvalidateForUser"/>).
///
/// Não diferencia admin — bypass é responsabilidade dos callers. Service
/// responde apenas sobre vínculos concretos em aihub.user_projects.
/// </summary>
public sealed class PgUserMembershipService : IUserMembershipService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IUserDirectory _directory;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public PgUserMembershipService(NpgsqlDataSource dataSource, IUserDirectory directory, IMemoryCache cache)
    {
        _dataSource = dataSource;
        _directory = directory;
        _cache = cache;
    }

    public async Task<IReadOnlyList<string>> GetVisibleProjectIdsAsync(string externalUserId, string tenantId, CancellationToken ct = default)
    {
        var user = await _directory.GetByExternalIdAsync(externalUserId, tenantId, ct);
        if (user is null) return Array.Empty<string>();

        var cacheKey = VisibleProjectsKey(tenantId, externalUserId);
        if (_cache.TryGetValue<IReadOnlyList<string>>(cacheKey, out var cached) && cached is not null)
            return cached;

        var projects = await LoadProjectIdsAsync(user.Id, ct);
        _cache.Set(cacheKey, projects, CacheTtl);
        return projects;
    }

    public async Task<bool> IsAuthorizedAsync(string externalUserId, string tenantId, string projectId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(projectId)) return false;

        var visible = await GetVisibleProjectIdsAsync(externalUserId, tenantId, ct);
        return visible.Contains(projectId, StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> GetProjectsForUserAsync(Guid userId, CancellationToken ct = default)
        => await LoadProjectIdsAsync(userId, ct);

    public async Task AssignProjectsAsync(Guid userId, IReadOnlyList<string> projectIds, string actorExternalUserId, CancellationToken ct = default)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.Transaction = tx;
            deleteCmd.CommandText = """
                DELETE FROM aihub.user_projects
                WHERE "UserId" = @userId AND NOT ("ProjectId" = ANY(@keepIds))
                """;
            deleteCmd.Parameters.AddWithValue("userId", userId);
            deleteCmd.Parameters.AddWithValue("keepIds", projectIds.ToArray());
            await deleteCmd.ExecuteNonQueryAsync(ct);
        }

        if (projectIds.Count > 0)
        {
            // INSERT idempotente: ON CONFLICT DO NOTHING preserva GrantedAt/GrantedBy
            // originais quando o vínculo já existia. Só linhas novas recebem
            // o GrantedBy desta chamada — vínculos pré-existentes mantêm a história.
            await using var insertCmd = conn.CreateCommand();
            insertCmd.Transaction = tx;
            insertCmd.CommandText = """
                INSERT INTO aihub.user_projects ("UserId", "ProjectId", "GrantedAt", "GrantedBy")
                SELECT @userId, unnest(@projectIds), NOW(), @actor
                ON CONFLICT ("UserId", "ProjectId") DO NOTHING
                """;
            insertCmd.Parameters.AddWithValue("userId", userId);
            insertCmd.Parameters.AddWithValue("projectIds", projectIds.ToArray());
            insertCmd.Parameters.AddWithValue("actor", (object?)actorExternalUserId ?? DBNull.Value);
            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);

        // Invalida caches do usuário afetado — próximo request enxerga o set novo.
        var user = await _directory.GetByIdAsync(userId, ct);
        if (user is not null)
            InvalidateForUser(user.TenantId, user.ExternalUserId);
    }

    public void InvalidateForUser(string tenantId, string externalUserId)
        => _cache.Remove(VisibleProjectsKey(tenantId, externalUserId));

    private async Task<IReadOnlyList<string>> LoadProjectIdsAsync(Guid userId, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT "ProjectId" FROM aihub.user_projects
            WHERE "UserId" = @userId ORDER BY "ProjectId"
            """;
        cmd.Parameters.AddWithValue("userId", userId);

        var results = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            results.Add(reader.GetString(0));
        return results;
    }

    private static string VisibleProjectsKey(string tenantId, string externalUserId)
        => $"membership:visible:{tenantId}:{externalUserId}";
}
