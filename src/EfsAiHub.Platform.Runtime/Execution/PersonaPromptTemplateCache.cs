using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Events;
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

public sealed class PersonaPromptTemplateCache : IPersonaPromptTemplateCache, IDisposable
{
    /// <summary>Identificador do cache no <see cref="ICacheInvalidationBus"/>.</summary>
    public const string CacheName = "persona-tpl";

    private static readonly TimeSpan L1Ttl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan L2Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);
    private const string RedisKeyPrefix = "persona-tpl:";

    private readonly IServiceProvider _sp;
    private readonly IEfsRedisCache _redis;
    private readonly ICacheInvalidationBus _invalidationBus;
    private readonly ILogger<PersonaPromptTemplateCache> _logger;

    private readonly ConcurrentDictionary<string, (PersonaPromptTemplate? Value, DateTime ExpiresAt)> _local =
        new(StringComparer.Ordinal);

    private readonly Timer _sweepTimer;
    private readonly IDisposable _invalidationSubscription;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public PersonaPromptTemplateCache(
        IServiceProvider sp,
        IEfsRedisCache redis,
        ICacheInvalidationBus invalidationBus,
        ILogger<PersonaPromptTemplateCache> logger)
    {
        _sp = sp;
        _redis = redis;
        _invalidationBus = invalidationBus;
        _logger = logger;

        // Sweep background de entries expiradas — evita growth unbounded do L1
        // em pods de longa duração. Roda a cada 30s (metade do TTL mais curto).
        _sweepTimer = new Timer(_ => SweepExpired(), null, SweepInterval, SweepInterval);

        // Subscribe em invalidações cross-pod. Echo do próprio pod é filtrado
        // pelo bus — aqui só recebemos eventos de OUTROS pods.
        _invalidationSubscription = _invalidationBus.Subscribe(CacheName, key =>
        {
            _local.TryRemove(key, out _);
            _logger.LogDebug("[PersonaTemplateCache] L1 invalidado cross-pod para scope={Scope}.", key);
            return Task.CompletedTask;
        });
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
        catch (JsonException ex)
        {
            // Payload corrompido no Redis: loga warning (não debug) porque
            // indica schema drift ou dados ruins — refetch do PG.
            _logger.LogWarning(ex,
                "[PersonaTemplateCache] L2 payload inválido pra scope={Scope}; refetch do PG.", scope);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
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
            catch (Exception ex) when (IsRedisTransient(ex))
            {
                _logger.LogDebug(ex, "[PersonaTemplateCache] Redis write falhou pra scope={Scope}.", scope);
            }

            return fresh;
        }
        catch (Exception ex) when (IsPgTransient(ex))
        {
            // Só engolimos falhas de transport do PG (timeout, conexão caída).
            // Bugs lógicos (NullRef, ArgumentException) propagam — não podem
            // virar "template ausente" silencioso.
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
            catch (Exception ex) when (IsRedisTransient(ex))
            {
                _logger.LogDebug(ex, "[PersonaTemplateCache] Invalidate L2 falhou.");
            }

            // Cross-pod: outros pods limpam L1 deles. Best-effort.
            await _invalidationBus.PublishInvalidateAsync(CacheName, scope);
            return;
        }

        var keys = _local.Keys.ToList();
        _local.Clear();
        foreach (var k in keys)
        {
            try { await _redis.RemoveAsync(RedisKeyPrefix + k); }
            catch (Exception ex) when (IsRedisTransient(ex))
            {
                _logger.LogDebug(ex, "[PersonaTemplateCache] Flush L2 falhou para scope={Scope}.", k);
            }
            await _invalidationBus.PublishInvalidateAsync(CacheName, k);
        }
    }

    /// <summary>
    /// Remove entries expiradas do L1. Roda em background via <see cref="_sweepTimer"/>.
    /// O(N) sobre o dict; aceitável porque sweep é raro (30s) e o dict é bounded
    /// por scopes ativos em ≤60s (TTL positive) ou ≤30s (negative).
    /// </summary>
    private void SweepExpired()
    {
        if (_disposed) return;
        var now = DateTime.UtcNow;
        var removed = 0;
        foreach (var kv in _local)
        {
            if (kv.Value.ExpiresAt <= now && _local.TryRemove(kv.Key, out _))
                removed++;
        }
        if (removed > 0)
            _logger.LogDebug("[PersonaTemplateCache] Sweep removeu {Removed} entries (size={Size}).",
                removed, _local.Count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
        _invalidationSubscription.Dispose();
    }

    // Redis (StackExchange.Redis) não expõe exception base própria útil — whitelist
    // por exclusão. JsonException e OperationCanceledException não são transport,
    // não devem virar "Redis flaky".
    private static bool IsRedisTransient(Exception ex) =>
        ex is not JsonException && ex is not OperationCanceledException;

    // PG (Npgsql) + EF Core lançam tipicamente DbException / TimeoutException
    // em transport. Bugs lógicos (ArgumentException, NullRef, InvalidOperationException)
    // NÃO são transport — devem propagar pra 500. OperationCanceledException
    // também propaga: cancelamento do caller não é "PG flaky".
    private static bool IsPgTransient(Exception ex) =>
        ex is TimeoutException
        || ex is System.Data.Common.DbException;
}
