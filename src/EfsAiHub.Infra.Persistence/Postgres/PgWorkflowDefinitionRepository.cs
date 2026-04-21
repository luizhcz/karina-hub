using System.Text.Json;
using EfsAiHub.Core.Orchestration.Workflows;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgWorkflowDefinitionRepository : IWorkflowDefinitionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IWorkflowVersionRepository _versionRepo;
    private readonly IEfsRedisCache _cache;
    private readonly ILogger<PgWorkflowDefinitionRepository> _logger;
    private readonly TimeSpan _ttl;

    // Chave Redis: efs-ai-hub:workflow-def:{id} (prefixo aplicado pelo wrapper).
    private const string CacheKeyPrefix = "workflow-def:";

    public PgWorkflowDefinitionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IWorkflowVersionRepository versionRepo,
        IEfsRedisCache cache,
        IConfiguration config,
        ILogger<PgWorkflowDefinitionRepository> logger)
    {
        _factory = factory;
        _versionRepo = versionRepo;
        _cache = cache;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    public async Task<WorkflowDefinition?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + id;

        // Read-through cache.
        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
            return JsonSerializer.Deserialize<WorkflowDefinition>(cached, JsonDefaults.Domain);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowDefinitions.FindAsync([id], ct);
        if (row is null) return null;

        await _cache.SetStringAsync(cacheKey, row.Data, _ttl);
        return JsonSerializer.Deserialize<WorkflowDefinition>(row.Data, JsonDefaults.Domain);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.WorkflowDefinitions.ToListAsync(ct);
        return rows
            .Select(r => JsonSerializer.Deserialize<WorkflowDefinition>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<WorkflowDefinition> UpsertAsync(
        WorkflowDefinition definition, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(definition, JsonDefaults.Domain);
        var now = DateTime.UtcNow;

        var existing = await ctx.WorkflowDefinitions.FindAsync([definition.Id], ct);
        if (existing is null)
        {
            ctx.WorkflowDefinitions.Add(new WorkflowDefinitionRow
            {
                Id = definition.Id,
                Name = definition.Name,
                Data = data,
                ProjectId = definition.ProjectId,
                Visibility = definition.Visibility,
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        else
        {
            existing.Name = definition.Name;
            existing.Data = data;
            existing.Visibility = definition.Visibility;
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

        // Regra: atualiza o Redis APENAS se a chave já existia (SetIfExistsAsync).
        // Se não existia, deixa para o próximo GetByIdAsync popular via read-through.
        await _cache.SetIfExistsAsync(CacheKeyPrefix + definition.Id, data, _ttl);

        return definition;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowDefinitions.FindAsync([id], ct);
        if (row is null) return false;
        ctx.WorkflowDefinitions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        await _cache.RemoveAsync(CacheKeyPrefix + id);
        return true;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.WorkflowDefinitions.AnyAsync(r => r.Id == id, ct);
    }

    /// <summary>
    /// Lista workflows visíveis para um projeto: workflows do próprio projeto + workflows globais.
    /// Ignora HasQueryFilter para consultar cross-project (global visibility).
    /// </summary>
    public async Task<IReadOnlyList<WorkflowDefinition>> ListVisibleAsync(
        string projectId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.WorkflowDefinitions
            .IgnoreQueryFilters()
            .Where(r => r.ProjectId == projectId || r.Visibility == "global")
            .ToListAsync(ct);

        return rows
            .Select(r => JsonSerializer.Deserialize<WorkflowDefinition>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

}
