using System.Text.Json;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.DependencyInjection;

namespace EfsAiHub.Platform.Runtime.Execution;

/// <summary>
/// Cache de preços de modelos LLM com Redis como camada compartilhada cross-pod.
/// Consultado em hot-path pelo <see cref="Factories.TokenTrackingChatClient"/> para
/// calcular custo incremental em USD sem roundtrip ao Postgres a cada chamada.
///
/// Fluxo: local in-memory (5min) → Redis (5min) → Postgres fallback → populate ambos.
/// <see cref="InvalidateAsync"/> é chamado pelo ModelPricingController após Upsert/Delete,
/// garantindo que todos os pods reflitam o novo preço imediatamente.
/// </summary>
public interface IModelPricingCache
{
    /// <summary>
    /// Retorna o preço vigente para um modelo. null se não houver entrada ativa
    /// (enforcement de custo é ignorado silenciosamente nesse caso).
    /// </summary>
    ValueTask<ModelPricing?> GetAsync(string modelId, CancellationToken ct = default);

    /// <summary>
    /// Invalida o cache para um modelo específico (ou todos se null).
    /// Deve ser chamado após Upsert/Delete de preços.
    /// </summary>
    Task InvalidateAsync(string? modelId = null);
}

public sealed class ModelPricingCache : IModelPricingCache, IDisposable
{
    /// <summary>Identificador no <see cref="ICacheInvalidationBus"/> (F2).</summary>
    public const string CacheName = "model-pricing";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromSeconds(30);
    private const string RedisKeyPrefix = "pricing:";

    private readonly IServiceProvider _sp;
    private readonly IEfsRedisCache _redis;
    private readonly ICacheInvalidationBus _invalidationBus;
    private readonly ILogger<ModelPricingCache> _logger;

    // Local in-memory cache for hot-path (avoids Redis roundtrip on every token tracking call)
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ModelPricing? Value, DateTime ExpiresAt)> _local = new(StringComparer.OrdinalIgnoreCase);

    private readonly IDisposable _invalidationSubscription;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ModelPricingCache(
        IServiceProvider sp,
        IEfsRedisCache redis,
        ICacheInvalidationBus invalidationBus,
        ILogger<ModelPricingCache> logger)
    {
        _sp = sp;
        _redis = redis;
        _invalidationBus = invalidationBus;
        _logger = logger;

        _invalidationSubscription = _invalidationBus.Subscribe(CacheName, key =>
        {
            _local.TryRemove(key, out _);
            return Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _invalidationSubscription.Dispose();
    }

    public async ValueTask<ModelPricing?> GetAsync(string modelId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // 1. Local in-memory (fastest — no I/O)
        if (_local.TryGetValue(modelId, out var localEntry) && localEntry.ExpiresAt > now)
            return localEntry.Value;

        // 2. Redis (shared cross-pod)
        try
        {
            var redisKey = $"{RedisKeyPrefix}{modelId}";
            var cached = await _redis.GetStringAsync(redisKey);
            if (cached is not null)
            {
                var pricing = cached == "null"
                    ? null
                    : JsonSerializer.Deserialize<ModelPricing>(cached, JsonOpts);
                _local[modelId] = (pricing, now + Ttl);
                return pricing;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ModelPricingCache] Redis read failed for '{Model}', falling back to Postgres.", modelId);
        }

        // 3. Postgres fallback → populate Redis + local
        try
        {
            using var scope = _sp.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IModelPricingRepository>();
            var all = await repo.GetAllAsync(ct);
            var match = all
                .Where(p => string.Equals(p.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                .Where(p => p.EffectiveFrom <= now && (p.EffectiveTo is null || p.EffectiveTo >= now))
                .OrderByDescending(p => p.EffectiveFrom)
                .FirstOrDefault();

            _local[modelId] = (match, now + Ttl);

            // Populate Redis (fire-and-forget — cache miss is tolerable)
            try
            {
                var redisKey = $"{RedisKeyPrefix}{modelId}";
                var json = match is null ? "null" : JsonSerializer.Serialize(match, JsonOpts);
                await _redis.SetStringAsync(redisKey, json, Ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[ModelPricingCache] Redis write failed for '{Model}'.", modelId);
            }

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ModelPricingCache] Postgres fallback failed for '{Model}'.", modelId);
            _local[modelId] = (null, now + ErrorTtl);
            return null;
        }
    }

    public async Task InvalidateAsync(string? modelId = null)
    {
        if (modelId is not null)
        {
            _local.TryRemove(modelId, out _);
            try { await _redis.RemoveAsync($"{RedisKeyPrefix}{modelId}"); }
            catch (Exception ex) { _logger.LogWarning(ex, "[ModelPricingCache] Redis invalidate failed for '{Model}'.", modelId); }
            await _invalidationBus.PublishInvalidateAsync(CacheName, modelId);
        }
        else
        {
            // Invalidate all — clear local and remove known Redis keys
            var keys = _local.Keys.ToList();
            _local.Clear();
            foreach (var key in keys)
            {
                try { await _redis.RemoveAsync($"{RedisKeyPrefix}{key}"); }
                catch (Exception ex) { _logger.LogDebug(ex, "[ModelPricingCache] Best-effort Redis remove failed for '{Key}'.", key); }
                await _invalidationBus.PublishInvalidateAsync(CacheName, key);
            }
        }
    }
}
