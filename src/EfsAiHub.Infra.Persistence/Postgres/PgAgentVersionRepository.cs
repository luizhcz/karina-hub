using System.Text.Json;
using EfsAiHub.Core.Agents;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append-only store de snapshots imutáveis de agente.
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
        // Defesa em profundidade: rejeita snapshots com invariantes violadas
        // antes de tocar o DB (ex: BreakingChange=true sem ChangeReason).
        version.EnsureInvariants();

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
            Snapshot = JsonSerializer.Serialize(version, JsonDefaults.Domain),
            BreakingChange = version.BreakingChange,
            SchemaVersion = version.SchemaVersion,
        };

        ctx.AgentVersions.Add(row);
        await ctx.SaveChangesAsync(ct);
        return version;
    }

    public async Task<AgentVersion?> GetAncestorBreakingAsync(
        string agentDefinitionId,
        int fromRevisionExclusive,
        int toRevisionInclusive,
        CancellationToken ct = default)
    {
        if (toRevisionInclusive <= fromRevisionExclusive)
            return null;

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentVersions
            .Where(r => r.AgentDefinitionId == agentDefinitionId
                        && r.Revision > fromRevisionExclusive
                        && r.Revision <= toRevisionInclusive
                        && r.BreakingChange == true)
            .OrderBy(r => r.Revision)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<IReadOnlyList<(string AgentVersionId, string AgentDefinitionId)>> ListOrphanVersionsAsync(
        int limit = 50,
        CancellationToken ct = default)
    {
        if (limit <= 0) return Array.Empty<(string, string)>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // IgnoreQueryFilters porque agent_definitions tem HasQueryFilter (project-scoped).
        // Health check precisa enxergar orphans cross-project pra dashboards de ops.
        var existingAgentIds = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .Select(a => a.Id)
            .ToHashSetAsync(ct);

        var orphans = await ctx.AgentVersions
            .Where(v => !existingAgentIds.Contains(v.AgentDefinitionId))
            .OrderByDescending(v => v.CreatedAt)
            .Take(limit)
            .Select(v => new { v.AgentVersionId, v.AgentDefinitionId })
            .ToListAsync(ct);

        return orphans
            .Select(o => (o.AgentVersionId, o.AgentDefinitionId))
            .ToList();
    }

    public async Task<int> CountRetiredVersionsAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var retiredLabel = nameof(AgentVersionStatus.Retired);
        return await ctx.AgentVersions
            .CountAsync(v => v.Status == retiredLabel, ct);
    }

    public async Task<AgentVersion> ResolveEffectiveAsync(
        string agentDefinitionId,
        string pinnedVersionId,
        CancellationToken ct = default)
    {
        var pinned = await GetByIdAsync(pinnedVersionId, ct)
            ?? throw new InvalidOperationException(
                $"AgentVersion '{pinnedVersionId}' (pin) referenciada não foi encontrada.");

        if (!string.Equals(pinned.AgentDefinitionId, agentDefinitionId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"AgentVersion '{pinnedVersionId}' não pertence ao agent '{agentDefinitionId}'.");

        var current = await GetCurrentAsync(agentDefinitionId, ct);
        // Sem version Published — retorna o pin (caller pinou em estado pré-publish).
        if (current is null)
            return pinned;

        // Pin >= current: snapshot pinado já é o mais novo (ou igual).
        if (pinned.Revision >= current.Revision)
            return pinned;

        // Pin < current: tem breaking entre os dois? Se sim, fica no pin (exact).
        // Senão, propaga current (patch).
        var breaking = await GetAncestorBreakingAsync(
            agentDefinitionId, pinned.Revision, current.Revision, ct);

        return breaking is null ? current : pinned;
    }

    private static AgentVersion Deserialize(AgentVersionRow row)
    {
        var version = JsonSerializer.Deserialize<AgentVersion>(row.Snapshot, JsonDefaults.Domain);
        if (version is not null)
        {
            // Promoted columns são source of truth pra BreakingChange/SchemaVersion
            // (snapshot JSON pode estar desatualizado em rows antigas pré-feature).
            // SchemaVersion na coluna é NOT NULL DEFAULT 1 — confiamos no schema.
            return version with
            {
                BreakingChange = row.BreakingChange,
                SchemaVersion = row.SchemaVersion,
            };
        }

        // Fallback defensivo — snapshot corrompido ou JSON null literal. Workflows pinados
        // executam com esqueleto + defaults inseguros, então alimenta alerta sev1.
        EfsAiHub.Infra.Observability.MetricsRegistry.AgentVersionLosslessRoundtripFailures.Add(1,
            new KeyValuePair<string, object?>("agent_version_id", row.AgentVersionId));
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
            ContentHash: row.ContentHash,
            BreakingChange: row.BreakingChange,
            SchemaVersion: row.SchemaVersion);
    }
}
