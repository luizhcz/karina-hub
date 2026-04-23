using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Platform.Runtime.Execution;

/// <summary>
/// Cache de preços de Document Intelligence (USD por página). Estrutura análoga
/// ao <see cref="ModelPricingCache"/>, mas a chave é (ModelId, Provider) em vez
/// de apenas ModelId — DI tem múltiplos modelos prebuilt por provider.
///
/// Fluxo: local in-memory (5min) → Redis (5min) → Postgres fallback → populate ambos.
/// <see cref="InvalidateAsync"/> é chamado pelo controller admin após Upsert/Delete.
/// </summary>
public interface IDocumentIntelligencePricingCache
{
    /// <summary>
    /// Retorna o preço vigente para (modelId, provider). null se não houver entrada
    /// ativa — nesse caso o executor usa fallback hardcoded (ver DocumentIntelligenceFunctions).
    /// </summary>
    ValueTask<DocumentIntelligencePricing?> GetAsync(
        string modelId, string provider, CancellationToken ct = default);

    /// <summary>
    /// Invalida o cache para (modelId, provider) específico (ou todos se null).
    /// </summary>
    Task InvalidateAsync(string? modelId = null, string? provider = null);
}

public sealed class DocumentIntelligencePricingCache : IDocumentIntelligencePricingCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorTtl = TimeSpan.FromSeconds(30);
    private const string RedisKeyPrefix = "di-pricing:";

    private readonly IServiceProvider _sp;
    private readonly IEfsRedisCache _redis;
    private readonly ILogger<DocumentIntelligencePricingCache> _logger;

    private readonly ConcurrentDictionary<string, (DocumentIntelligencePricing? Value, DateTime ExpiresAt)> _local =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public DocumentIntelligencePricingCache(
        IServiceProvider sp,
        IEfsRedisCache redis,
        ILogger<DocumentIntelligencePricingCache> logger)
    {
        _sp = sp;
        _redis = redis;
        _logger = logger;
    }

    public async ValueTask<DocumentIntelligencePricing?> GetAsync(
        string modelId, string provider, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var key = CompositeKey(modelId, provider);

        if (_local.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            return entry.Value;

        try
        {
            var redisKey = $"{RedisKeyPrefix}{key}";
            var cached = await _redis.GetStringAsync(redisKey);
            if (cached is not null)
            {
                var pricing = cached == "null"
                    ? null
                    : JsonSerializer.Deserialize<DocumentIntelligencePricing>(cached, JsonOpts);
                _local[key] = (pricing, now + Ttl);
                return pricing;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocIntelPricingCache] Redis read failed for '{Key}', falling back to Postgres.", key);
        }

        try
        {
            using var scope = _sp.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IDocumentIntelligencePricingRepository>();
            var match = await repo.GetCurrentAsync(modelId, provider, ct);
            _local[key] = (match, now + Ttl);

            try
            {
                var redisKey = $"{RedisKeyPrefix}{key}";
                var json = match is null ? "null" : JsonSerializer.Serialize(match, JsonOpts);
                await _redis.SetStringAsync(redisKey, json, Ttl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[DocIntelPricingCache] Redis write failed for '{Key}'.", key);
            }

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DocIntelPricingCache] Postgres fallback failed for '{Key}'.", key);
            _local[key] = (null, now + ErrorTtl);
            return null;
        }
    }

    public async Task InvalidateAsync(string? modelId = null, string? provider = null)
    {
        if (modelId is not null && provider is not null)
        {
            var key = CompositeKey(modelId, provider);
            _local.TryRemove(key, out _);
            try { await _redis.RemoveAsync($"{RedisKeyPrefix}{key}"); }
            catch (Exception ex) { _logger.LogWarning(ex, "[DocIntelPricingCache] Redis invalidate failed for '{Key}'.", key); }
        }
        else
        {
            // Invalida tudo
            var keys = _local.Keys.ToList();
            _local.Clear();
            foreach (var k in keys)
            {
                try { await _redis.RemoveAsync($"{RedisKeyPrefix}{k}"); }
                catch (Exception ex) { _logger.LogDebug(ex, "[DocIntelPricingCache] Best-effort Redis remove failed for '{Key}'.", k); }
            }
        }
    }

    private static string CompositeKey(string modelId, string provider) => $"{modelId}|{provider}";
}
