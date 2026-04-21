using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using EfsAiHub.Host.Api.Chat.AgUi.Models;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Gerencia estado compartilhado entre agente e frontend por conversa (thread).
/// L1: MemoryCache in-memory com sliding expiration (acesso rápido durante streaming).
/// L2: Redis via IAgUiStateStore (cross-pod reconnect, TTL 2h).
/// </summary>
public sealed class AgUiStateManager
{
    private readonly IMemoryCache _cache;
    private readonly IAgUiStateStore _store;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(2);
    private static readonly TimeSpan L1SlidingExpiration = TimeSpan.FromMinutes(30);
    private const int MaxStateSizeBytes = 32 * 1024; // 32KB hard cap

    public AgUiStateManager(IMemoryCache cache, IAgUiStateStore store)
    {
        _cache = cache;
        _store = store;
    }

    public async Task<AgUiSharedState> GetOrCreateAsync(string threadId, JsonElement? initialState = null)
    {
        if (_cache.TryGetValue<AgUiSharedState>(CacheKey(threadId), out var cached) && cached is not null)
            return cached;

        // Tentar restaurar do Redis (reconnect cross-pod)
        var persisted = await _store.GetAsync(threadId);
        var state = new AgUiSharedState(persisted ?? initialState);
        SetCache(threadId, state);
        return state;
    }

    /// <summary>Agente atualiza estado (via workflow/execution).</summary>
    /// <exception cref="InvalidOperationException">Quando o state excede o limite de 32KB.</exception>
    public async Task<AgUiEvent?> SetAgentValueAsync(string threadId, string path, JsonElement value)
    {
        if (!_cache.TryGetValue<AgUiSharedState>(CacheKey(threadId), out var state) || state is null)
            return null;

        var delta = state.SetValue(path, value);

        // Guard: rejeita writes que excedem o hard cap
        var snapshot = state.GetSnapshot();
        var snapshotSize = System.Text.Encoding.UTF8.GetByteCount(snapshot.GetRawText());

        if (snapshotSize > MaxStateSizeBytes)
            throw new InvalidOperationException(
                $"SharedState para thread '{threadId}' excede o limite de {MaxStateSizeBytes / 1024}KB ({snapshotSize / 1024}KB). Reduza o volume de dados armazenados.");

        await _store.SaveAsync(threadId, snapshot, DefaultTtl);

        return new AgUiEvent
        {
            Type = "STATE_DELTA",
            Delta = delta
        };
    }

    /// <summary>Flush estado atual para Redis (chamado antes de encerrar stream).</summary>
    public async Task FinalizeAsync(string threadId)
    {
        if (_cache.TryGetValue<AgUiSharedState>(CacheKey(threadId), out var state) && state is not null)
            await _store.SaveAsync(threadId, state.GetSnapshot(), DefaultTtl);
    }

    /// <summary>Limpa estado quando conversa termina.</summary>
    public async Task RemoveAsync(string threadId)
    {
        _cache.Remove(CacheKey(threadId));
        await _store.DeleteAsync(threadId);
    }

    private void SetCache(string threadId, AgUiSharedState state)
    {
        var opts = new MemoryCacheEntryOptions { SlidingExpiration = L1SlidingExpiration };
        _cache.Set(CacheKey(threadId), state, opts);
    }

    private static string CacheKey(string threadId) => $"agui:state:{threadId}";
}
