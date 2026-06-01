using EfsAiHub.Core.Abstractions.RouterQuickActions;
using EfsAiHub.Core.Agents.RouterQuickActions;
using Microsoft.Extensions.Caching.Memory;

namespace EfsAiHub.Platform.Runtime.RouterQuickActions;

/// <summary>
/// Implementação de <see cref="IRouterQuickActionMatcher"/> com cache em
/// IMemoryCache (TTL 5min, refresh em background no miss). Ordena os entries
/// uma única vez ao popular o cache pra que cada match seja O(N) com N pequeno
/// (dezenas de entries por Router).
///
/// <para>
/// Cache key = (RouterId, ProjectId, TenantId) — granularidade da unique
/// constraint no banco. Invalidação via <see cref="InvalidateRouter"/> remove
/// só a entry afetada (chamado pelo CRUD do controller).
/// </para>
/// </summary>
public sealed class RouterQuickActionMatcher : IRouterQuickActionMatcher
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly IRouterQuickActionRepository _repo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<RouterQuickActionMatcher> _logger;

    public RouterQuickActionMatcher(
        IRouterQuickActionRepository repo,
        IMemoryCache cache,
        ILogger<RouterQuickActionMatcher> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<RouterQuickActionMatch?> TryMatchAsync(
        string routerId,
        string tenantId,
        string projectId,
        string userContent,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(routerId) || string.IsNullOrWhiteSpace(userContent))
            return null;

        var entries = await GetSortedEntriesAsync(routerId, tenantId, projectId, ct);
        if (entries.Count == 0) return null;

        var normalized = QuickActionPatternMatcher.Normalize(userContent);
        if (string.IsNullOrEmpty(normalized)) return null;

        // Pre-ordenado por especificidade desc — primeiro match wins.
        foreach (var entry in entries)
        {
            if (QuickActionPatternMatcher.Matches(entry.Pattern, normalized))
            {
                _logger.LogDebug(
                    "[QuickAction] hit — router='{RouterId}' pattern='{Pattern}' intent='{Intent}'.",
                    routerId, entry.Pattern, entry.Intent);
                return new RouterQuickActionMatch(entry.Intent, entry.Pattern, entry.DisplayText);
            }
        }

        return null;
    }

    public void InvalidateRouter(string routerId, string tenantId, string projectId)
    {
        _cache.Remove(BuildKey(routerId, tenantId, projectId));
    }

    private async Task<IReadOnlyList<RouterQuickAction>> GetSortedEntriesAsync(
        string routerId, string tenantId, string projectId, CancellationToken ct)
    {
        var key = BuildKey(routerId, tenantId, projectId);
        if (_cache.TryGetValue<IReadOnlyList<RouterQuickAction>>(key, out var cached) && cached is not null)
            return cached;

        var raw = await _repo.ListByRouterAsync(routerId, tenantId, ct);
        // Filtra por projeto manualmente — repo retorna por tenant + router (sem
        // ProjectContext disponível na call do matcher).
        var sorted = raw
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => QuickActionPatternMatcher.Specificity(e.Pattern))
            .ThenBy(e => QuickActionPatternMatcher.HasWildcard(e.Pattern) ? 1 : 0)
            .ToList();

        _cache.Set(key, (IReadOnlyList<RouterQuickAction>)sorted, CacheTtl);
        return sorted;
    }

    private static string BuildKey(string routerId, string tenantId, string projectId)
        => $"quickactions:{tenantId}:{projectId}:{routerId}";
}
