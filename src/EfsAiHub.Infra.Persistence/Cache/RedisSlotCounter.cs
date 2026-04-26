using EfsAiHub.Core.Abstractions.Execution;
using StackExchange.Redis;

namespace EfsAiHub.Infra.Persistence.Cache;

/// <summary>
/// Implementação Redis de <see cref="IDistributedSlotCounter"/> usando Lua scripts atômicos.
/// - TryAcquire: GET → se &lt; max → INCR + EXPIRE → retorna novo valor; senão -1
/// - Release: DECR → se &lt; 0 → SET 0
/// - TTL de segurança (configurável): auto-libera se pod morrer sem Release.
/// </summary>
public sealed class RedisSlotCounter : IDistributedSlotCounter
{
    private readonly IEfsRedisCache _cache;

    private static readonly LuaScript AcquireScript = LuaScript.Prepare(@"
local current = tonumber(redis.call('GET', @key) or '0')
if current < tonumber(@maxSlots) then
    local newVal = redis.call('INCR', @key)
    redis.call('PEXPIRE', @key, @ttlMs)
    return newVal
end
return -1
");

    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(@"
local current = redis.call('DECR', @key)
if current < 0 then
    redis.call('SET', @key, '0')
    return 0
end
return current
");

    public RedisSlotCounter(IEfsRedisCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> TryAcquireAsync(string scope, int maxSlots, TimeSpan ttl)
    {
        var fullKey = _cache.BuildKey($"slots:{scope}");
        var ttlMs = (long)ttl.TotalMilliseconds;

        var result = (long)(await _cache.Database.ScriptEvaluateAsync(
            AcquireScript,
            new
            {
                key = (RedisKey)fullKey,
                maxSlots = maxSlots,
                ttlMs = ttlMs
            }))!;

        return result >= 0;
    }

    public async Task ReleaseAsync(string scope)
    {
        var fullKey = _cache.BuildKey($"slots:{scope}");

        await _cache.Database.ScriptEvaluateAsync(
            ReleaseScript,
            new { key = (RedisKey)fullKey });
    }

    public async Task<int> GetActiveCountAsync(string scope)
    {
        var fullKey = _cache.BuildKey($"slots:{scope}");
        var value = await _cache.Database.StringGetAsync(fullKey);
        return value.HasValue && int.TryParse((string?)value, out var count) ? count : 0;
    }
}
