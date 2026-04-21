using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Core.Abstractions.Projects;
using StackExchange.Redis;

namespace EfsAiHub.Platform.Runtime.Guards;

/// <summary>
/// Rate limiter por projeto usando Redis Sorted Set sliding window.
/// Lê MaxRequestsPerMinute de ProjectSettings. Se null/0, sem enforcement.
/// Mesmo padrão atômico do ChatRateLimiter (Lua script).
/// </summary>
public sealed class ProjectRateLimiter
{
    private readonly IEfsRedisCache _cache;
    private readonly IProjectRepository _projectRepo;

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

    public ProjectRateLimiter(IEfsRedisCache cache, IProjectRepository projectRepo)
    {
        _cache = cache;
        _projectRepo = projectRepo;
    }

    /// <summary>
    /// Tenta adquirir um slot de rate limit para o projeto.
    /// Retorna true se permitido, false se excedido (429).
    /// </summary>
    public async Task<bool> TryAcquireAsync(string projectId, CancellationToken ct = default)
    {
        var project = await _projectRepo.GetByIdAsync(projectId, ct);
        var maxRpm = project?.Settings?.MaxRequestsPerMinute;

        // Sem limite configurado = sem enforcement
        if (maxRpm is null or 0)
            return true;

        var fullKey = _cache.BuildKey($"rl:project:{projectId}");
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        const long windowMs = 60_000; // 1 minuto

        var member = $"{now}:{Guid.NewGuid():N}";

        var result = (long)(await _cache.Database.ScriptEvaluateAsync(
            SlidingWindowScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
                windowMs = windowMs,
                maxCount = maxRpm.Value,
                member = (RedisValue)member
            }))!;

        return result == 1;
    }
}
