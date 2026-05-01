using System.Text.Json;
using EfsAiHub.Core.Agents.Skills;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Repositório de Skills com dual-write append-only para SkillVersion,
/// mesma mecânica de <see cref="PgAgentDefinitionRepository"/>.
/// </summary>
public sealed class PgSkillRepository : ISkillRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ISkillVersionRepository _versionRepo;
    private readonly ILogger<PgSkillRepository> _logger;

    public PgSkillRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ISkillVersionRepository versionRepo,
        ILogger<PgSkillRepository> logger)
    {
        _factory = factory;
        _versionRepo = versionRepo;
        _logger = logger;
    }

    public async Task<Skill?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FirstOrDefaultAsync respeita HasQueryFilter; FindAsync bypassa.
        var row = await ctx.Skills.FirstOrDefaultAsync(r => r.Id == id, ct);
        return row is null ? null : JsonSerializer.Deserialize<Skill>(row.Data, JsonDefaults.Domain);
    }

    public async Task<Skill?> GetByIdForOwnerAsync(
        string id, string ownerProjectId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // Cross-project: bypass query filter + filtro explícito por owner. Usado pelo
        // SkillResolver quando agent global referencia skill local do owner.
        var row = await ctx.Skills
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.ProjectId == ownerProjectId, ct);
        return row is null ? null : JsonSerializer.Deserialize<Skill>(row.Data, JsonDefaults.Domain);
    }

    public async Task<IReadOnlyList<Skill>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.Skills.ToListAsync(ct);
        return rows
            .Select(r => JsonSerializer.Deserialize<Skill>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<IReadOnlyList<Skill>> GetAllAsync(int page, int pageSize, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.Skills
            .OrderBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return rows
            .Select(r => JsonSerializer.Deserialize<Skill>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.Skills.CountAsync(ct);
    }

    public async Task<IReadOnlyList<Skill>> GetAllAcrossProjectsAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.Skills.IgnoreQueryFilters().ToListAsync(ct);
        return rows
            .Select(r => JsonSerializer.Deserialize<Skill>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<Skill> UpsertAsync(Skill skill, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(skill, JsonDefaults.Domain);
        var hash = Skill.ComputeHash(skill);
        var now = DateTime.UtcNow;

        var existing = await ctx.Skills.FindAsync([skill.Id], ct);
        if (existing is null)
        {
            ctx.Skills.Add(new SkillRow
            {
                Id = skill.Id,
                Name = skill.Name,
                Data = data,
                ContentHash = hash,
                CreatedAt = now,
                UpdatedAt = now,
                ProjectId = skill.ProjectId
            });
        }
        else
        {
            existing.Name = skill.Name;
            existing.Data = data;
            existing.ContentHash = hash;
            existing.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync(ct);

        // Dual-write append-only (idempotente por ContentHash).
        try
        {
            var revision = await _versionRepo.GetNextRevisionAsync(skill.Id, ct);
            var snapshot = new SkillVersion(
                SkillVersionId: Guid.NewGuid().ToString("N"),
                SkillId: skill.Id,
                Revision: revision,
                CreatedAt: now,
                CreatedBy: null,
                ChangeReason: null,
                Snapshot: skill,
                ContentHash: hash);
            await _versionRepo.AppendAsync(snapshot, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PgSkillRepository] Falha ao gravar SkillVersion snapshot para '{SkillId}'.", skill.Id);
        }

        return skill;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.Skills.FindAsync([id], ct);
        if (row is null) return false;
        ctx.Skills.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }
}
