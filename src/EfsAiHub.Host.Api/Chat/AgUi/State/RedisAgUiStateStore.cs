using System.Text.Json;
using EfsAiHub.Infra.Persistence.Cache;

namespace EfsAiHub.Host.Api.Chat.AgUi.State;

/// <summary>
/// Redis-backed state store para AG-UI shared state.
/// Key: efs:agui:state:{threadId}. TTL: 2 horas.
/// Permite reconnect cross-pod sem perda de estado.
/// </summary>
public sealed class RedisAgUiStateStore : IAgUiStateStore
{
    private readonly IEfsRedisCache _redis;
    private const string KeyPrefix = "agui:state:";

    public RedisAgUiStateStore(IEfsRedisCache redis)
    {
        _redis = redis;
    }

    public async Task<JsonElement?> GetAsync(string threadId)
    {
        var json = await _redis.GetStringAsync($"{KeyPrefix}{threadId}");
        if (json is null) return null;

        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(string threadId, JsonElement snapshot, TimeSpan ttl)
    {
        var json = snapshot.GetRawText();
        await _redis.SetStringAsync($"{KeyPrefix}{threadId}", json, ttl);
    }

    public async Task DeleteAsync(string threadId)
    {
        await _redis.RemoveAsync($"{KeyPrefix}{threadId}");
    }
}
