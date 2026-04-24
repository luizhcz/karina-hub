using System.Text.Json;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgAgentPromptRepository : IAgentPromptRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgAgentPromptRepository> _logger;
    private readonly IEfsRedisCache _cache;
    private readonly TimeSpan _cacheTtl;

    // Chave Redis: efs-ai-hub:agent-prompt:active:{agentId} (prefixo pelo wrapper).
    private const string CacheKeyPrefix = "agent-prompt:active:";

    private sealed record CachedPrompt(string Content, string VersionId);

    public PgAgentPromptRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgAgentPromptRepository> logger,
        IEfsRedisCache cache,
        IConfiguration config)
    {
        _factory = factory;
        _logger = logger;
        _cache = cache;
        _cacheTtl = TimeSpan.FromSeconds(config.GetValue<int>("Redis:DefinitionCacheTtlSeconds", 300));
    }

    public void InvalidateCache(string agentId)
        => _cache.RemoveAsync(CacheKeyPrefix + agentId).GetAwaiter().GetResult();

    public async Task<string?> GetActivePromptAsync(string agentId, CancellationToken ct = default)
    {
        var result = await GetActivePromptWithVersionAsync(agentId, ct);
        return result?.Content;
    }

    public async Task<(string Content, string VersionId)?> GetActivePromptWithVersionAsync(string agentId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyPrefix + agentId;

        var cached = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            var hit = JsonSerializer.Deserialize<CachedPrompt>(cached);
            if (hit is not null)
            {
                _logger.LogDebug("[PromptRepo] Cache hit para agente '{AgentId}'.", agentId);
                return (hit.Content, hit.VersionId);
            }
        }

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var active = await ctx.AgentPromptVersions
            .Where(r => r.AgentId == agentId && r.IsActive)
            .OrderByDescending(r => r.RowId)
            .FirstOrDefaultAsync(ct);

        if (active is null)
        {
            _logger.LogDebug("[PromptRepo] Nenhum prompt ativo para agente '{AgentId}'.", agentId);
            return null;
        }

        var payload = JsonSerializer.Serialize(new CachedPrompt(active.Content, active.VersionId));
        await _cache.SetStringAsync(cacheKey, payload, _cacheTtl);
        return (active.Content, active.VersionId);
    }

    public async Task<IReadOnlyList<AgentPromptVersion>> ListVersionsAsync(
        string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.AgentPromptVersions
            .Where(r => r.AgentId == agentId)
            .OrderBy(r => r.VersionId)
            .ToListAsync(ct);

        return rows
            .Select(r => new AgentPromptVersion(r.VersionId, r.Content, r.IsActive))
            .ToList();
    }

    public async Task SaveVersionAsync(
        string agentId, string versionId, string content, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(versionId))
            throw new ArgumentException("versionId não pode ser vazio.", nameof(versionId));

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var existing = await ctx.AgentPromptVersions
            .FirstOrDefaultAsync(r => r.AgentId == agentId && r.VersionId == versionId, ct);

        if (existing is null)
        {
            ctx.AgentPromptVersions.Add(new AgentPromptVersionRow
            {
                AgentId = agentId,
                VersionId = versionId,
                Content = content,
                IsActive = false,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Content = content;
            if (existing.IsActive) InvalidateCache(agentId);
        }

        await ctx.SaveChangesAsync(ct);
        _logger.LogInformation("[PromptRepo] Versão '{VersionId}' gravada para agente '{AgentId}'.",
            versionId, agentId);
    }

    public async Task SetMasterAsync(
        string agentId, string versionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var target = await ctx.AgentPromptVersions
            .FirstOrDefaultAsync(r => r.AgentId == agentId && r.VersionId == versionId, ct);

        if (target is null)
            throw new KeyNotFoundException(
                $"Versão '{versionId}' não existe para o agente '{agentId}'. Grave-a primeiro.");

        var allVersions = await ctx.AgentPromptVersions
            .Where(r => r.AgentId == agentId)
            .ToListAsync(ct);

        foreach (var v in allVersions)
            v.IsActive = v.VersionId == versionId;

        await ctx.SaveChangesAsync(ct);

        // Regra: atualiza no Redis APENAS se a chave já existe; caso contrário,
        // apenas remove (defensivo) e deixa o próximo Get popular via read-through.
        var cacheKey = CacheKeyPrefix + agentId;
        var payload = JsonSerializer.Serialize(new CachedPrompt(target.Content, target.VersionId));
        var updated = await _cache.SetIfExistsAsync(cacheKey, payload, _cacheTtl);
        if (!updated) await _cache.RemoveAsync(cacheKey);

        _logger.LogInformation("[PromptRepo] Master do agente '{AgentId}' movido para '{VersionId}'.",
            agentId, versionId);
    }

    public async Task DeleteVersionAsync(
        string agentId, string versionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentPromptVersions
            .FirstOrDefaultAsync(r => r.AgentId == agentId && r.VersionId == versionId, ct);

        if (row is null)
            throw new KeyNotFoundException($"Versão '{versionId}' não encontrada para o agente '{agentId}'.");

        if (row.IsActive)
            throw new InvalidOperationException(
                $"Não é possível apagar a versão ativa '{versionId}'. Mude o master primeiro.");

        ctx.AgentPromptVersions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        _logger.LogInformation("[PromptRepo] Versão '{VersionId}' removida do agente '{AgentId}'.",
            versionId, agentId);
    }

    public async Task ClearMasterAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var activeVersions = await ctx.AgentPromptVersions
            .Where(r => r.AgentId == agentId && r.IsActive)
            .ToListAsync(ct);

        foreach (var v in activeVersions)
            v.IsActive = false;

        await ctx.SaveChangesAsync(ct);
        await _cache.RemoveAsync(CacheKeyPrefix + agentId);

        _logger.LogInformation("[PromptRepo] Master do agente '{AgentId}' limpo — fallback para instructions.",
            agentId);
    }
}
