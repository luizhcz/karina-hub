using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Execution;

/// <summary>
/// Cache dos templates de persona. Segue o padrão de <see cref="ModelPricingCache"/>:
/// L1 in-memory (60s) → L2 Redis (5min) → L3 Postgres.
///
/// Interface separada do repository porque <c>PersonaPromptComposer</c> roda em
/// hot path (toda invocação de agente) — não pode hit DB cada turn.
/// </summary>
public interface IPersonaPromptTemplateCache
{
    ValueTask<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default);

    /// <summary>
    /// Invalida entrada específica ou tudo (null = flush). Chamado pelo
    /// admin controller após upsert/delete via UI.
    /// </summary>
    Task InvalidateAsync(string? scope = null);
}

public sealed class PersonaPromptTemplateCache : IPersonaPromptTemplateCache
{
    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(30);
    private const string RedisKeyPrefix = "persona-tpl:";

    private readonly IServiceProvider _sp;
    private readonly IEfsRedisCache _redis;
    private readonly ILogger<PersonaPromptTemplateCache> _logger;

    private readonly ConcurrentDictionary<string, (PersonaPromptTemplate? Value, DateTime ExpiresAt)> _local =
        new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PersonaPromptTemplateCache(
        IServiceProvider sp,
        IEfsRedisCache redis,
        ILogger<PersonaPromptTemplateCache> logger)
    {
        _sp = sp;
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<PersonaPromptTemplate?> GetByScopeAsync(string scope, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        if (_local.TryGetValue(scope, out var entry) && entry.ExpiresAt > now)
            return entry.Value;

        try
        {
            var cached = await _redis.GetStringAsync(RedisKeyPrefix + scope);
            if (cached is not null)
            {
                // "null" é sentinel pra scope inexistente — evita martelar o DB
                // a cada request quando admin ainda não cadastrou template.
                var parsed = cached == "null"
                    ? null
                    : JsonSerializer.Deserialize<PersonaPromptTemplate>(cached, JsonOpts);
                _local[scope] = (parsed, now + L1Ttl);
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[PersonaTemplateCache] Redis falhou para scope={Scope}; caindo pra PG.", scope);
        }

        try
        {
            using var s = _sp.CreateScope();
            var repo = s.ServiceProvider.GetRequiredService<IPersonaPromptTemplateRepository>();
            var fresh = await repo.GetByScopeAsync(scope, ct);

            var ttl = fresh is null ? NegativeTtl : L1Ttl;
            _local[scope] = (fresh, now + ttl);

            try
            {
                var json = fresh is null ? "null" : JsonSerializer.Serialize(fresh, JsonOpts);
                await _redis.SetStringAsync(RedisKeyPrefix + scope, json, fresh is null ? NegativeTtl : L2Ttl);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[PersonaTemplateCache] Redis write falhou pra scope={Scope}.", scope);
            }

            return fresh;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PersonaTemplateCache] PG fallback falhou pra scope={Scope}.", scope);
            _local[scope] = (null, now + NegativeTtl);
            return null;
        }
    }

    public async Task InvalidateAsync(string? scope = null)
    {
        if (scope is not null)
        {
            _local.TryRemove(scope, out _);
            try { await _redis.RemoveAsync(RedisKeyPrefix + scope); }
            catch (Exception ex) { _logger.LogDebug(ex, "[PersonaTemplateCache] Invalidate L2 falhou."); }
            return;
        }

        var keys = _local.Keys.ToList();
        _local.Clear();
        foreach (var k in keys)
        {
            try { await _redis.RemoveAsync(RedisKeyPrefix + k); }
            catch { /* best effort */ }
        }
    }
}
