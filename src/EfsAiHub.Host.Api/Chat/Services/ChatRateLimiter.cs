using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EfsAiHub.Host.Api.Services;

/// <summary>
/// Rate limiter distribuído por userId e por conversationId usando Redis Sorted Set
/// sliding window. Múltiplas instâncias da API compartilham o mesmo contador —
/// pré-requisito para escalar o chat horizontalmente.
///
/// Chaves Redis (prefixo aplicado pelo IEfsRedisCache):
///   efs-ai-hub:rl:chat:{userId}
///   efs-ai-hub:rl:chat:conv:{conversationId}
///
/// Implementação via Lua script atômico:
///   1. ZREMRANGEBYSCORE key -inf (now - window)  — remove timestamps fora da janela
///   2. ZCARD key                                 — conta tokens na janela
///   3. Se count < max: ZADD key now now; PEXPIRE; return 1
///      Senão: return 0
/// </summary>
public class ChatRateLimiter
{
    private readonly IEfsRedisCache _cache;
    private readonly int _maxMessages;
    private readonly int _windowSeconds;
    private readonly int _maxMessagesPerConversation;
    private readonly int _conversationWindowSeconds;

    private static readonly LuaScript SlidingWindowScript = LuaScript.Prepare(@"
local cutoff = tonumber(@now) - tonumber(@windowMs)
redis.call('ZREMRANGEBYSCORE', @key, '-inf', cutoff)
local count = redis.call('ZCARD', @key)
if count < tonumber(@maxCount) then
    redis.call('ZADD', @key, @now, @member)
    redis.call('PEXPIRE', @key, @windowMs)
    return 1
end
return 0
");

    public ChatRateLimiter(IEfsRedisCache cache, IOptions<ChatRateLimitOptions> options)
    {
        _cache = cache;
        var opts = options.Value;
        _maxMessages = opts.MaxMessages;
        _windowSeconds = opts.WindowSeconds;
        _maxMessagesPerConversation = opts.MaxMessagesPerConversation;
        _conversationWindowSeconds = opts.ConversationWindowSeconds;
    }

    /// <summary>
    /// Tenta registrar um request para o userId.
    /// Retorna true se dentro do limite, false se excedido (429).
    /// </summary>
    public Task<bool> TryAcquireAsync(string userId, CancellationToken ct = default)
        => TryAcquireInternalAsync($"rl:chat:{userId}", _maxMessages, _windowSeconds);

    /// <summary>
    /// Tenta registrar um request para um conversationId específico.
    /// Retorna true se dentro do limite, false se excedido (429).
    /// </summary>
    public Task<bool> TryAcquireForConversationAsync(string conversationId, CancellationToken ct = default)
        => TryAcquireInternalAsync($"rl:chat:conv:{conversationId}", _maxMessagesPerConversation, _conversationWindowSeconds);

    private async Task<bool> TryAcquireInternalAsync(string logicalKey, int maxMessages, int windowSeconds)
    {
        var fullKey = _cache.BuildKey(logicalKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)windowSeconds * 1000;

        // member único por request (evita colisões entre chamadas no mesmo ms)
        var member = $"{now}:{Guid.NewGuid():N}";

        var result = (long)(await _cache.Database.ScriptEvaluateAsync(
            SlidingWindowScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
                windowMs = windowMs,
                maxCount = maxMessages,
                member = (RedisValue)member
            }))!;

        return result == 1;
    }
}
