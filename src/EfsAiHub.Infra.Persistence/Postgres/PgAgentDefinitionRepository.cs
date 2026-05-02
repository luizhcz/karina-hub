using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
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
    private readonly IProjectRepository _projectRepo;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ISkillVersionRepository? _skillVersionRepo;
    private readonly IFunctionToolRegistry? _functionRegistry;
    private readonly ILogger<PgAgentDefinitionRepository> _logger;
    private readonly TimeSpan _ttl;

    // Cache tenant-aware: efs-ai-hub:agent-def:{tenantId}:{id}. Tenant A nunca compartilha
    // slot com tenant B — se algum caminho bypass query filter, cache não vaza.
    private const string CacheKeyPrefix = "agent-def:";
    private string CacheKey(string id) => $"{CacheKeyPrefix}{_tenantAccessor.Current.TenantId}:{id}";

    public PgAgentDefinitionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IEfsRedisCache cache,
        IAgentVersionRepository versionRepo,
        IAgentPromptRepository promptRepo,
        IProjectRepository projectRepo,
        ITenantContextAccessor tenantAccessor,
        ILogger<PgAgentDefinitionRepository> logger,
        IConfiguration config,
        ISkillVersionRepository? skillVersionRepo = null,
        IFunctionToolRegistry? functionRegistry = null)
    {
        _factory = factory;
        _cache = cache;
        _versionRepo = versionRepo;
        _promptRepo = promptRepo;
        _projectRepo = projectRepo;
        _tenantAccessor = tenantAccessor;
        _skillVersionRepo = skillVersionRepo;
        _functionRegistry = functionRegistry;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    /// <summary>
    /// Hidrata Visibility/ProjectId/TenantId da row sobre o domain — defesa contra JSON
    /// legado (sem esses campos) ou inconsistência transient. Row é source of truth.
    /// </summary>
    private static AgentDefinition Hydrate(AgentDefinitionRow row, AgentDefinition def)
    {
        def.ProjectId = row.ProjectId;
        def.TenantId = row.TenantId;
        def.Visibility = row.Visibility;
        return def;
    }

    public async Task<AgentDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(id);

        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<AgentDefinition>(cached, JsonDefaults.Domain);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FirstOrDefaultAsync respeita HasQueryFilter (project OR global+tenant) — caller
        // de outro tenant não vê. Nunca usar FindAsync aqui (bypass de filter).
        var row = await ctx.AgentDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return null;

        await _cache.SetStringAsync(cacheKey, row.Data, _ttl);
        var def = JsonSerializer.Deserialize<AgentDefinition>(row.Data, JsonDefaults.Domain)!;
        return Hydrate(row, def);
    }

    public async Task<IReadOnlyList<AgentDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // ToListAsync respeita HasQueryFilter — retorna agents do projeto atual + globais do tenant.
        var rows = await ctx.AgentDefinitions.ToListAsync(ct);
        return rows
            .Select(r => Hydrate(r, JsonSerializer.Deserialize<AgentDefinition>(r.Data, JsonDefaults.Domain)!))
            .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<AgentDefinition> UpsertAsync(
        AgentDefinition definition,
        CancellationToken ct = default,
        bool? breakingChange = null,
        string? changeReason = null,
        string? createdBy = null)
    {
        // Carimba FingerprintHash em cada function tool com o hash canônico
        // (sha256 de name|description|jsonSchema) do AIFunction atualmente registrado.
        // Snapshots subsequentes de AgentVersion herdam esse hash via AgentVersion.FromDefinition.
        definition = StampFunctionFingerprints(definition);

        // Lookup do tenant do project owner — denormaliza pra row pra que o
        // query filter no DbContext consiga enforçar tenant boundary sem JOIN.
        var ownerProject = await _projectRepo.GetByIdAsync(definition.ProjectId, ct);
        var tenantId = ownerProject?.TenantId ?? "default";
        // Sincroniza no domain pra que serializações futuras carreguem o valor correto.
        definition.TenantId = tenantId;

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(definition, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters: precisamos achar a row mesmo quando o caller não enxerga
        // o agent pelo filter atual (raro — apenas com bypass owner-gated upstream).
        var existing = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == definition.Id, ct);
        var visibilityChanged = existing is not null && existing.Visibility != definition.Visibility;

        if (existing is null)
        {
            ctx.AgentDefinitions.Add(new AgentDefinitionRow
            {
                Id = definition.Id,
                Name = definition.Name,
                Data = data,
                ProjectId = definition.ProjectId,
                Visibility = definition.Visibility,
                TenantId = tenantId,
                CreatedAt = now,
                UpdatedAt = now,
                // ADR 0015 — propaga as 3 colunas regression. Sem isso o
                // AgentRegressionConfigController.Update salvaria silenciosamente
                // só o Data JSON e o autotrigger nunca dispararia.
                RegressionTestSetId = definition.RegressionTestSetId,
                RegressionEvaluatorConfigVersionId = definition.RegressionEvaluatorConfigVersionId
            });
        }
        else
        {
            existing.Name = definition.Name;
            existing.Data = data;
            existing.Visibility = definition.Visibility;
            existing.TenantId = tenantId;
            existing.UpdatedAt = now;
            existing.RegressionTestSetId = definition.RegressionTestSetId;
            existing.RegressionEvaluatorConfigVersionId = definition.RegressionEvaluatorConfigVersionId;
        }

        await ctx.SaveChangesAsync(ct);

        // Cache tenant-aware: invalida total em mudança de visibility (outros projetos
        // do tenant podem estar com cache stale); senão atualiza só se já existia.
        if (visibilityChanged)
            await _cache.RemoveAsync(CacheKey(definition.Id));
        else
            await _cache.SetIfExistsAsync(CacheKey(definition.Id), data, _ttl);

        // Dual-write: append de snapshot imutável atômico.
        // Idempotente por ContentHash: upserts consecutivos sem mudança real não criam nova revision.
        try
        {
            (string Content, string VersionId)? prompt = null;
            try { prompt = await _promptRepo.GetActivePromptWithVersionAsync(definition.Id, ct); }
            catch { /* best-effort — prompt ainda pode não existir no momento do upsert inicial */ }

            var revision = await _versionRepo.GetNextRevisionAsync(definition.Id, ct);

            // Materializa SkillVersionId concreto para cada SkillRef;
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
                createdBy: createdBy,
                changeReason: changeReason,
                skillRefs: materializedSkills,
                breakingChange: breakingChange);

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
        // Respeita filter — caller só consegue deletar agents visíveis ao project/tenant atual.
        var row = await ctx.AgentDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.AgentDefinitions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKey(id));
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.AgentDefinitions.AnyAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Reconstrói <see cref="AgentDefinition"/> com o <c>FingerprintHash</c>
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

    public async Task<IReadOnlyList<(string AgentId, string MissingProjectId)>> ListOrphanGlobalAgentsAsync(
        int limit = 20, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // IgnoreQueryFilters: health check roda fora de scope HTTP (sem project/tenant válido).
        // Read-only, idempotente. LEFT JOIN seria mais limpo mas projects é internal — usamos
        // sub-query Any() inverso.
        var orphans = await ctx.AgentDefinitions
            .IgnoreQueryFilters()
            .Where(a => a.Visibility == "global"
                && !ctx.Projects.IgnoreQueryFilters().Any(p => p.Id == a.ProjectId))
            .OrderBy(a => a.Id)
            .Take(Math.Max(1, limit))
            .Select(a => new { a.Id, a.ProjectId })
            .ToListAsync(ct);

        return orphans.Select(o => (o.Id, o.ProjectId)).ToList();
    }
}
