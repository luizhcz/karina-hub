using System.Text.Json;
using EfsAiHub.Core.Agents;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Fase 1 — Append-only store de snapshots imutáveis de agente.
/// Idempotência por <see cref="AgentVersion.ContentHash"/>: se a última revision já carrega
/// o mesmo hash, retorna-a em vez de criar uma duplicata.
/// </summary>
public sealed class PgAgentVersionRepository : IAgentVersionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgAgentVersionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<AgentVersion?> GetByIdAsync(string agentVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentVersions.FindAsync([agentVersionId], ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<AgentVersion?> GetCurrentAsync(string agentDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentVersions
            .Where(r => r.AgentDefinitionId == agentDefinitionId && r.Status == "Published")
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<IReadOnlyList<AgentVersion>> ListByDefinitionAsync(
        string agentDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentVersions
            .Where(r => r.AgentDefinitionId == agentDefinitionId)
            .OrderByDescending(r => r.Revision)
            .ToListAsync(ct);
        return rows.Select(Deserialize).ToList();
    }

    public async Task<int> GetNextRevisionAsync(string agentDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var max = await ctx.AgentVersions
            .Where(r => r.AgentDefinitionId == agentDefinitionId)
            .Select(r => (int?)r.Revision)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<AgentVersion> AppendAsync(AgentVersion version, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // Idempotência por hash: se a última revision já carrega esse content hash, no-op.
        var last = await ctx.AgentVersions
            .Where(r => r.AgentDefinitionId == version.AgentDefinitionId)
            .OrderByDescending(r => r.Revision)
            .FirstOrDefaultAsync(ct);

        if (last is not null && last.ContentHash == version.ContentHash)
            return Deserialize(last);

        var row = new AgentVersionRow
        {
            AgentVersionId = version.AgentVersionId,
            AgentDefinitionId = version.AgentDefinitionId,
            Revision = version.Revision,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            ChangeReason = version.ChangeReason,
            Status = version.Status.ToString(),
            ContentHash = version.ContentHash,
            Snapshot = JsonSerializer.Serialize(version, JsonDefaults.Domain)
        };

        ctx.AgentVersions.Add(row);
        await ctx.SaveChangesAsync(ct);
        return version;
    }

    private static AgentVersion Deserialize(AgentVersionRow row)
    {
        var version = JsonSerializer.Deserialize<AgentVersion>(row.Snapshot, JsonDefaults.Domain);
        if (version is not null) return version;

        // Fallback defensivo — snapshot corrompido: reconstrói um esqueleto mínimo.
        return new AgentVersion(
            AgentVersionId: row.AgentVersionId,
            AgentDefinitionId: row.AgentDefinitionId,
            Revision: row.Revision,
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ChangeReason: row.ChangeReason,
            Status: Enum.TryParse<AgentVersionStatus>(row.Status, out var s) ? s : AgentVersionStatus.Published,
            PromptContent: null,
            PromptVersionId: null,
            Model: new AgentModelSnapshot("", null, null),
            Provider: new AgentProviderSnapshot("AzureFoundry", "ChatCompletion", null, false),
            ToolFingerprints: Array.Empty<ToolFingerprint>(),
            MiddlewarePipeline: Array.Empty<AgentMiddlewareSnapshot>(),
            OutputSchema: null,
            Resilience: null,
            CostBudget: null,
            SkillRefs: Array.Empty<EfsAiHub.Core.Agents.Skills.SkillRef>(),
            ContentHash: row.ContentHash);
    }
}
