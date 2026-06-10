using EfsAiHub.Core.Abstractions.Execution;
using StackExchange.Redis;

namespace EfsAiHub.Infra.Persistence.Cache;

/// <summary>
/// Implementação Redis de <see cref="IDistributedSlotCounter"/> usando um Sorted
/// Set com expiração POR SLOT (mesmo padrão atômico via Lua do
/// <c>ProjectRateLimiter</c>/<c>ChatRateLimiter</c>).
///
/// Cada slot adquirido é um membro do ZSET cujo score é o instante absoluto de
/// expiração (<c>now + ttl</c>). Toda operação poda membros já vencidos
/// (<c>ZREMRANGEBYSCORE -inf now</c>) antes de contar. Assim um slot de pod que
/// morreu sem Release expira SOZINHO no seu próprio score.
///
/// Por que não o <c>INCR/DECR</c> anterior: aquele contava num único valor e dava
/// <c>PEXPIRE</c> na chave INTEIRA a cada acquire. Slot órfão de pod morto virava
/// um <c>+1</c> fantasma que NUNCA expirava enquanto houvesse tráfego (todo acquire
/// renovava o TTL da chave toda), corroendo a capacidade efetiva até o gate viver
/// cheio. Com score por membro, nenhum acquire renova o vencimento de outro slot —
/// o fantasma some no seu TTL independente do tráfego.
///
/// Release é token-less (semântica de semáforo de contagem, sem identidade de
/// holder): remove um slot ativo via <c>ZPOPMIN</c> (o de vencimento mais próximo).
/// A contagem permanece íntegra porque cada acquire adiciona exatamente um membro
/// e cada release remove exatamente um.
///
/// Chave versionada (<c>slots:v2:{scope}</c>): o tipo do valor mudou de String
/// (<c>INCR/DECR</c> da versão anterior) para ZSET. Sem o <c>v2</c>, um <c>ZADD</c>
/// numa chave que ainda guarda a String antiga daria <c>WRONGTYPE</c> durante o
/// rollout. A chave antiga expira sozinha pelo TTL — nenhuma migração manual.
///
/// Requer Redis 5.0+ (<c>ZPOPMIN</c>).
/// </summary>
public sealed class RedisSlotCounter : IDistributedSlotCounter
{
    private readonly IEfsRedisCache _cache;

    // Poda vencidos, conta e — se houver folga — registra um slot com vencimento
    // próprio (now + ttl). PEXPIRE na chave só serve de GC pra ZSET ocioso; não
    // mantém slot fantasma vivo porque a poda é por score de cada membro.
    private static readonly LuaScript AcquireScript = LuaScript.Prepare(@"
redis.call('ZREMRANGEBYSCORE', @key, '-inf', @now)
local count = redis.call('ZCARD', @key)
if count < tonumber(@maxSlots) then
    redis.call('ZADD', @key, tonumber(@now) + tonumber(@ttlMs), @member)
    redis.call('PEXPIRE', @key, @ttlMs)
    return 1
end
return -1
");

    // Poda vencidos e libera um slot ativo (ZPOPMIN = no-op em ZSET vazio, então
    // over-release não quebra). Retorna a contagem resultante.
    private static readonly LuaScript ReleaseScript = LuaScript.Prepare(@"
redis.call('ZREMRANGEBYSCORE', @key, '-inf', @now)
redis.call('ZPOPMIN', @key)
return redis.call('ZCARD', @key)
");

    private static readonly LuaScript CountScript = LuaScript.Prepare(@"
redis.call('ZREMRANGEBYSCORE', @key, '-inf', @now)
return redis.call('ZCARD', @key)
");

    public RedisSlotCounter(IEfsRedisCache cache)
    {
        _cache = cache;
    }

    public async Task<bool> TryAcquireAsync(string scope, int maxSlots, TimeSpan ttl)
    {
        var fullKey = _cache.BuildKey($"slots:v2:{scope}");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ttlMs = (long)ttl.TotalMilliseconds;
        var member = $"{now}:{Guid.NewGuid():N}";

        var result = (long)(await _cache.Database.ScriptEvaluateAsync(
            AcquireScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
                ttlMs = ttlMs,
                maxSlots = maxSlots,
                member = (RedisValue)member,
            }))!;

        return result == 1;
    }

    public async Task ReleaseAsync(string scope)
    {
        var fullKey = _cache.BuildKey($"slots:v2:{scope}");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await _cache.Database.ScriptEvaluateAsync(
            ReleaseScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
            });
    }

    public async Task<int> GetActiveCountAsync(string scope)
    {
        var fullKey = _cache.BuildKey($"slots:v2:{scope}");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var count = (long)(await _cache.Database.ScriptEvaluateAsync(
            CountScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
            }))!;

        return (int)count;
    }
}
