using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Core.Abstractions.Projects;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgWorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IEfsRedisCache _cache;
    private readonly ILogger<PgWorkflowDefinitionRepository> _logger;
    private readonly TimeSpan _ttl;

    // Chave Redis tenant-aware: efs-ai-hub:workflow-def:{tenantId}:{id}.
    // Tenant A consulta workflow X num cache key separado de tenant B → impossível
    // vazar dados entre tenants via cache mesmo se algum caminho bypass query filter.
    private const string CacheKeyPrefix = "workflow-def:";
    private string CacheKey(string id) => $"{CacheKeyPrefix}{_tenantAccessor.Current.TenantId}:{id}";

    public PgWorkflowDefinitionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IWorkflowVersionRepository versionRepo,
        IProjectRepository projectRepo,
        ITenantContextAccessor tenantAccessor,
        IEfsRedisCache cache,
        IConfiguration config,
        ILogger<PgWorkflowDefinitionRepository> logger)
    {
        _factory = factory;
        _versionRepo = versionRepo;
        _projectRepo = projectRepo;
        _tenantAccessor = tenantAccessor;
        _cache = cache;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    /// <summary>
    /// Hidrata Visibility/ProjectId/TenantId da row sobre o domain (defesa contra JSON
    /// antigo ou inconsistência transient — a row é fonte de verdade desses 3 campos).
    /// </summary>
    private static WorkflowDefinition Hydrate(WorkflowDefinitionRow row, WorkflowDefinition def)
    {
        def.ProjectId = row.ProjectId;
        def.TenantId = row.TenantId;
        def.Visibility = row.Visibility;
        return def;
    }

    public async Task<WorkflowDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cacheKey = CacheKey(id);

        // Read-through cache (tenant-aware key — não vaza cross-tenant).
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<WorkflowDefinition>(cached, JsonDefaults.Domain);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FirstOrDefaultAsync respeita HasQueryFilter (project OR global+tenant).
        // FindAsync ignora query filter — usar APENAS em paths owner-gated (UpsertAsync).
        var row = await ctx.WorkflowDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return null;

        await _cache.SetStringAsync(cacheKey, row.Data, _ttl);
        var def = JsonSerializer.Deserialize<WorkflowDefinition>(row.Data, JsonDefaults.Domain)!;
        return Hydrate(row, def);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // ToListAsync respeita HasQueryFilter — só retorna workflows visíveis ao project/tenant atual.
        var rows = await ctx.WorkflowDefinitions.ToListAsync(ct);
        return rows
            .Select(r => Hydrate(r, JsonSerializer.Deserialize<WorkflowDefinition>(r.Data, JsonDefaults.Domain)!))
            .ToList();
    }

    public async Task<WorkflowDefinition> UpsertAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        // Lookup do tenant do project owner — denormaliza pra row pra que o
        // query filter no DbContext consiga enforçar tenant boundary sem JOIN.
        var ownerProject = await _projectRepo.GetByIdAsync(definition.ProjectId, ct);
        var tenantId = ownerProject?.TenantId ?? "default";
        // Sincroniza no domain pra que serializações futuras carreguem o valor correto.
        definition.TenantId = tenantId;

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(definition, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters no FindAsync: precisamos achar a row mesmo se o
        // visibility/tenant atual do caller não bate (ex: owner mudando seu próprio
        // workflow global; o filter sem ignore só pegaria se já fosse mesmo project).
        var existing = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == definition.Id, ct);
        var visibilityChanged = existing is not null && existing.Visibility != definition.Visibility;

        if (existing is null)
        {
            ctx.WorkflowDefinitions.Add(new WorkflowDefinitionRow
            {
                Id = definition.Id,
                Name = definition.Name,
                Data = data,
                ProjectId = definition.ProjectId,
                Visibility = definition.Visibility,
                TenantId = tenantId,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Name = definition.Name;
            existing.Data = data;
            existing.Visibility = definition.Visibility;
            existing.TenantId = tenantId;
            existing.UpdatedAt = now;
        }

        await ctx.SaveChangesAsync(ct);

        // Dual-write: append versão imutável (best-effort, não bloqueia upsert).
        try
        {
            var revision = await _versionRepo.GetNextRevisionAsync(definition.Id, ct);
            var version = WorkflowVersion.FromDefinition(definition, revision);
            await _versionRepo.AppendAsync(version, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Falha ao criar WorkflowVersion para '{WorkflowId}'. Definição foi salva normalmente.",
                definition.Id);
        }

        // Cache tenant-aware: chave por (tenantId, id). Em mudança de visibility, força
        // invalidação total (RemoveAsync) — projetos do mesmo tenant podem estar com cache
        // stale. Outros tenants nem têm cache nessa chave, então não há invalidação cross-tenant
        // a fazer (fail-safe by design).
        if (visibilityChanged)
            await _cache.RemoveAsync(CacheKey(definition.Id));
        else
            await _cache.SetIfExistsAsync(CacheKey(definition.Id), data, _ttl);

        return definition;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // Respeita query filter: caller só consegue deletar workflows visíveis ao project/tenant atual.
        var row = await ctx.WorkflowDefinitions.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null) return false;
        ctx.WorkflowDefinitions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKey(id));
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.WorkflowDefinitions.AnyAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Lista workflows visíveis para um projeto: próprio projeto + workflows globais
    /// do mesmo tenant. Tenant boundary é enforced via filtro explícito (sem cross-tenant).
    /// </summary>
    public async Task<IReadOnlyList<WorkflowDefinition>> ListVisibleAsync(
        string projectId, string tenantId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(r => r.ProjectId == projectId
                || (r.Visibility == "global" && r.TenantId == tenantId))
            .ToListAsync(ct);

        return rows
            .Select(r => Hydrate(r, JsonSerializer.Deserialize<WorkflowDefinition>(r.Data, JsonDefaults.Domain)!))
            .ToList();
    }
}
