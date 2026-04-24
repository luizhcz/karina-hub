using System.Text.Json;
using EfsAiHub.Core.Agents.Skills;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append-only store de snapshots imutáveis de skills. Idempotente por
/// <see cref="SkillVersion.ContentHash"/>: upserts sem mudança real não duplicam revision.
/// </summary>
public sealed class PgSkillVersionRepository : ISkillVersionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgSkillVersionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<SkillVersion?> GetByIdAsync(string skillVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.SkillVersions.FindAsync([skillVersionId], ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<SkillVersion?> GetCurrentAsync(string skillId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.SkillVersions
            .Where(r => r.SkillId == skillId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<IReadOnlyList<SkillVersion>> ListBySkillAsync(
        string skillId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.SkillVersions
            .Where(r => r.SkillId == skillId)
            .OrderByDescending(r => r.Revision)
            .ToListAsync(ct);
        return rows.Select(Deserialize).ToList();
    }

    public async Task<int> GetNextRevisionAsync(string skillId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var max = await ctx.SkillVersions
            .Where(r => r.SkillId == skillId)
            .Select(r => (int?)r.Revision)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<SkillVersion> AppendAsync(SkillVersion version, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var last = await ctx.SkillVersions
            .Where(r => r.SkillId == version.SkillId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);

        if (last is not null && last.ContentHash == version.ContentHash)
            return Deserialize(last);

        var row = new SkillVersionRow
        {
            SkillVersionId = version.SkillVersionId,
            SkillId = version.SkillId,
            Revision = version.Revision,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            ChangeReason = version.ChangeReason,
            ContentHash = version.ContentHash,
            Snapshot = JsonSerializer.Serialize(version, JsonDefaults.Domain)
        };

        ctx.SkillVersions.Add(row);
        await ctx.SaveChangesAsync(ct);
        return version;
    }

    private static SkillVersion Deserialize(SkillVersionRow row)
    {
        var version = JsonSerializer.Deserialize<SkillVersion>(row.Snapshot, JsonDefaults.Domain);
        if (version is not null) return version;

        // Fallback defensivo: snapshot corrompido → shell mínimo.
        return new SkillVersion(
            SkillVersionId: row.SkillVersionId,
            SkillId: row.SkillId,
            Revision: row.Revision,
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ChangeReason: row.ChangeReason,
            Snapshot: new Skill { Id = row.SkillId, Name = row.SkillId },
            ContentHash: row.ContentHash);
    }
}
