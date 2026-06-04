using EfsAiHub.Core.Abstractions.Identity;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Platform.Runtime.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EfsAiHub.Host.Api.Middleware;

/// <summary>
/// Sliding window rate limiter pro polling de jobs standalone
/// (<c>GET /api/aihub/responses/{jobId}</c>). Limite por projectId — clientes
/// do mesmo projeto compartilham a janela, o que aceita variação de pods e
/// IPs. Quando o caller não tem projectId resolvido, cai em scope <c>anon</c>
/// (cota global pequena pra forçar identidade autenticada).
///
/// Implementação compartilhada com <c>ChatRateLimiter</c>: ZSet sliding window
/// via Lua atômico.
///
/// Aplicado APENAS na rota <c>/api/aihub/responses/*</c> com método GET — POST
/// não passa pelo limite (já é caro por outro lado via cota de slots).
/// </summary>
public sealed class ResponsePollingRateLimitMiddleware
{
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

    private const int WindowSeconds = 60;
    private readonly RequestDelegate _next;
    private readonly StandalonePoolsOptions _options;

    public ResponsePollingRateLimitMiddleware(
        RequestDelegate next,
        IOptions<StandalonePoolsOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IEfsRedisCache cache,
        IProjectContextAccessor projectAccessor)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var isPolling =
            context.Request.Method == HttpMethods.Get &&
            path.StartsWith("/api/aihub/responses/", StringComparison.OrdinalIgnoreCase);

        if (!isPolling || !_options.Enabled)
        {
            await _next(context);
            return;
        }

        var scope = projectAccessor.Current.ProjectId;
        // Caller sem projeto resolvido tenta polar dados sem ownership — falha
        // 401 em vez de cair num scope global "anon" compartilhado por todos
        // os clientes não-autenticados (que seria denial-of-service trivial).
        if (string.IsNullOrWhiteSpace(scope))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Identidade do projeto não resolvida. Envie x-project-id ou autentique a request."
            });
            return;
        }

        var allowed = await TryAcquireAsync(cache, scope);
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "30";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Rate limit do polling excedido. Tente novamente em 30s.",
                retryAfterSeconds = 30
            });
            return;
        }

        await _next(context);
    }

    private async Task<bool> TryAcquireAsync(IEfsRedisCache cache, string scope)
    {
        var logicalKey = $"rl:responses:{scope}";
        var fullKey = cache.BuildKey(logicalKey);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)WindowSeconds * 1000;
        var member = $"{now}:{Guid.NewGuid():N}";

        var maxCount = Math.Max(1, _options.PollingRateLimitPerMinute);

        var result = (long)(await cache.Database.ScriptEvaluateAsync(
            SlidingWindowScript,
            new
            {
                key = (RedisKey)fullKey,
                now = now,
                windowMs = windowMs,
                maxCount = maxCount,
                member = (RedisValue)member
            }))!;

        return result == 1;
    }
}
