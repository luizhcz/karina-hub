using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgAgentDefinitionRepository : IAgentDefinitionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IEfsRedisCache _cache;
    private readonly IAgentVersionRepository _versionRepo;
    private readonly IAgentPromptRepository _promptRepo;
    private readonly ISkillVersionRepository? _skillVersionRepo;
    private readonly IFunctionToolRegistry? _functionRegistry;
    private readonly ILogger<PgAgentDefinitionRepository> _logger;
    private readonly TimeSpan _ttl;

    // Chave Redis: efs-ai-hub:agent-def:{id} (prefixo aplicado pelo wrapper).
    private const string CacheKeyPrefix = "agent-def:";

    public PgAgentDefinitionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IEfsRedisCache cache,
        IAgentVersionRepository versionRepo,
        IAgentPromptRepository promptRepo,
        ILogger<PgAgentDefinitionRepository> logger,
        IConfiguration config,
        ISkillVersionRepository? skillVersionRepo = null,
        IFunctionToolRegistry? functionRegistry = null)
    {
        _factory = factory;
        _cache = cache;
        _versionRepo = versionRepo;
        _promptRepo = promptRepo;
        _skillVersionRepo = skillVersionRepo;
        _functionRegistry = functionRegistry;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    public async Task<AgentDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + id;

        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<AgentDefinition>(cached, JsonDefaults.Domain);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDefinitions.FindAsync([id], ct);
        if (row is null) return null;

        await _cache.SetStringAsync(cacheKey, row.Data, _ttl);
        return JsonSerializer.Deserialize<AgentDefinition>(row.Data, JsonDefaults.Domain);
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentDefinitions.ToListAsync(ct);
        return rows
            .Select(r => JsonSerializer.Deserialize<AgentDefinition>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<AgentDefinition> UpsertAsync(
        AgentDefinition definition, CancellationToken ct = default)
    {
        // Fase 6 — carimba FingerprintHash em cada function tool com o hash canônico
        // (sha256 de name|description|jsonSchema) do AIFunction atualmente registrado.
        // Snapshots subsequentes de AgentVersion herdam esse hash via AgentVersion.FromDefinition.
        definition = StampFunctionFingerprints(definition);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(definition, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        var existing = await ctx.AgentDefinitions.FindAsync([definition.Id], ct);
        if (existing is null)
        {
            ctx.AgentDefinitions.Add(new AgentDefinitionRow
            {
                Id = definition.Id,
                Name = definition.Name,
                Data = data,
                ProjectId = definition.ProjectId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Name = definition.Name;
            existing.Data = data;
            existing.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync(ct);

        // Atualiza no Redis APENAS se a chave já existia.
        await _cache.SetIfExistsAsync(CacheKeyPrefix + definition.Id, data, _ttl);

        // Fase 1 — dual-write: append de snapshot imutável atômico.
        // Idempotente por ContentHash: upserts consecutivos sem mudança real não criam nova revision.
        try
        {
            (string Content, string VersionId)? prompt = null;
            try { prompt = await _promptRepo.GetActivePromptWithVersionAsync(definition.Id, ct); }
            catch { /* best-effort — prompt ainda pode não existir no momento do upsert inicial */ }

            var revision = await _versionRepo.GetNextRevisionAsync(definition.Id, ct);

            // Fase 3 — materializa SkillVersionId concreto para cada SkillRef;
            // garante que rollback resolva a versão exata da skill em uso no momento do publish.
            IReadOnlyList<SkillRef>? materializedSkills = null;
            if (_skillVersionRepo is not null && definition.SkillRefs.Count > 0)
            {
                var resolved = new List<SkillRef>(definition.SkillRefs.Count);
                foreach (var r in definition.SkillRefs)
                {
                    if (!string.IsNullOrEmpty(r.SkillVersionId))
                    {
                        resolved.Add(r);
                        continue;
                    }
                    try
                    {
                        var current = await _skillVersionRepo.GetCurrentAsync(r.SkillId, ct);
                        resolved.Add(current is null ? r : new SkillRef(r.SkillId, current.SkillVersionId));
                    }
                    catch
                    {
                        resolved.Add(r); // best-effort
                    }
                }
                materializedSkills = resolved;
            }

            var snapshot = AgentVersion.FromDefinition(
                definition,
                revision,
                promptContent: prompt?.Content,
                promptVersionId: prompt?.VersionId,
                skillRefs: materializedSkills);

            await _versionRepo.AppendAsync(snapshot, ct);
        }
        catch (Exception ex)
        {
            // Não bloqueia a escrita primária — logamos para retomada manual/backfill.
            _logger.LogWarning(ex,
                "[PgAgentDefinitionRepository] Falha ao gravar AgentVersion snapshot para '{AgentId}'.",
                definition.Id);
        }

        return definition;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentDefinitions.FindAsync([id], ct);
        if (row is null) return false;
        ctx.AgentDefinitions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKeyPrefix + id);
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentDefinitions.AnyAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Fase 6 — reconstrói <see cref="AgentDefinition"/> com o <c>FingerprintHash</c>
    /// atual de cada function tool no registry. Tools desconhecidas ou sem registry
    /// preservam o hash existente (ou null).
    /// </summary>
    private AgentDefinition StampFunctionFingerprints(AgentDefinition definition)
    {
        if (_functionRegistry is null || definition.Tools.Count == 0) return definition;

        var changed = false;
        var rebuilt = new List<AgentToolDefinition>(definition.Tools.Count);
        foreach (var tool in definition.Tools)
        {
            if (!string.Equals(tool.Type, "function", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(tool.Name))
            {
                rebuilt.Add(tool);
                continue;
            }

            var latest = _functionRegistry.GetLatestFingerprint(tool.Name!);
            if (latest is null || latest == tool.FingerprintHash)
            {
                rebuilt.Add(tool);
                continue;
            }

            changed = true;
            rebuilt.Add(new AgentToolDefinition
            {
                Type = tool.Type,
                Name = tool.Name,
                RequiresApproval = tool.RequiresApproval,
                FingerprintHash = latest,
                ServerLabel = tool.ServerLabel,
                ServerUrl = tool.ServerUrl,
                AllowedTools = tool.AllowedTools,
                RequireApproval = tool.RequireApproval,
                Headers = tool.Headers,
                ConnectionId = tool.ConnectionId
            });
        }

        if (!changed) return definition;

        return new AgentDefinition
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            Model = definition.Model,
            Provider = definition.Provider,
            Instructions = definition.Instructions,
            Tools = rebuilt,
            StructuredOutput = definition.StructuredOutput,
            Middlewares = definition.Middlewares,
            Resilience = definition.Resilience,
            CostBudget = definition.CostBudget,
            SkillRefs = definition.SkillRefs,
            Metadata = definition.Metadata,
            CreatedAt = definition.CreatedAt,
            UpdatedAt = definition.UpdatedAt
        };
    }

    public async Task<IReadOnlySet<string>> GetExistingIdsAsync(
        IEnumerable<string> ids, CancellationToken ct = default)
    {
        var arr = ids.ToArray();
        if (arr.Length == 0) return new HashSet<string>();
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var found = await ctx.AgentDefinitions
            .Where(r => arr.Contains(r.Id))
            .Select(r => r.Id)
            .ToListAsync(ct);
        return found.ToHashSet();
    }
}
