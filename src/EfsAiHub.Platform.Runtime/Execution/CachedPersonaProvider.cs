using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Events;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Execution;

/// <summary>
/// Decorator de cache para <see cref="IPersonaProvider"/>:
///  - L1: dicionário in-memory local por processo (hot path, zero I/O).
///  - L2: Redis (compartilhado cross-pod, TTL configurável).
///  - L3: <see cref="HttpPersonaProvider"/> (API externa) — que JÁ nunca lança.
///
/// <para>Proteções importantes:</para>
/// <list type="bullet">
///   <item><b>Single-flight</b> em cache miss: N requests concorrentes pro mesmo
///   (userId, userType) disparam UMA chamada HTTP. Implementado via
///   <see cref="Lazy{T}"/> em dicionário paralelo de tasks em voo.</item>
///   <item><b>Eviction periódica</b> do L1: um timer remove entries expiradas
///   a cada ~30s. Sem isso, o dicionário cresce sem teto em pods de longa duração
///   com base de usuários grande (1M users ≈ 1M entries mortas).</item>
///   <item><b>Polimorfismo na deserialização</b>: a chave Redis carrega o
///   <c>userType</c>, então deserializamos direto pro subtipo concreto
///   (<see cref="ClientPersona"/> vs <see cref="AdminPersona"/>).</item>
/// </list>
///
/// Responsabilidade ÚNICA: caching. Política de recovery mora no provider HTTP
/// a jusante — decorator não engole exceptions do inner.
/// </summary>
public sealed class CachedPersonaProvider : IPersonaProvider, IDisposable
{
    /// <summary>Identificador do cache no <see cref="ICacheInvalidationBus"/>.</summary>
    public const string CacheName = "persona";

    private const string RedisKeyPrefix = "persona:";

    // Sweep intervalo: metade do TTL L1 é uma heurística razoável — garante
    // que entries expiradas somem em ≤1.5× TTL no pior caso.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly HttpPersonaProvider _inner;
    private readonly IEfsRedisCache _redis;
    private readonly ICacheInvalidationBus _invalidationBus;
    private readonly PersonaApiOptions _options;
    private readonly ILogger<CachedPersonaProvider> _logger;

    private readonly ConcurrentDictionary<string, CacheEntry> _local =
        new(StringComparer.Ordinal);

    // Single-flight: tasks em voo por chave. Lazy garante que só 1 factory roda.
    // Remove-se a entrada após resolver (sucesso ou falha) pra não reter Task
    // finalizado — próximo caller acha no L1/L2 ou dispara novo.
    private readonly ConcurrentDictionary<string, Lazy<Task<UserPersona>>> _inFlight =
        new(StringComparer.Ordinal);

    private readonly Timer _sweepTimer;
    private readonly IDisposable _invalidationSubscription;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public CachedPersonaProvider(
        HttpPersonaProvider inner,
        IEfsRedisCache redis,
        ICacheInvalidationBus invalidationBus,
        IOptions<PersonaApiOptions> options,
        ILogger<CachedPersonaProvider> logger)
    {
        _inner = inner;
        _redis = redis;
        _invalidationBus = invalidationBus;
        _options = options.Value;
        _logger = logger;

        // Timer com due=SweepInterval pra não bloquear a primeira request do pod.
        _sweepTimer = new Timer(_ => SweepExpired(), null, SweepInterval, SweepInterval);

        // Cross-pod invalidation: quando admin invalidar em outro pod,
        // limpamos L1 daqui também.
        _invalidationSubscription = _invalidationBus.Subscribe(CacheName, key =>
        {
            _local.TryRemove(key, out _);
            _inFlight.TryRemove(key, out _);
            return Task.CompletedTask;
        });
    }

    public async Task<UserPersona> ResolveAsync(
        string userId,
        string userType,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return UserPersonaFactory.Anonymous(userId ?? "", userType);

        var key = CompositeKey(userId, userType);
        var now = DateTime.UtcNow;

        // L1: local in-memory (zero I/O).
        if (_local.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            return entry.Value;

        // L2 + L3 sob single-flight: um caller dispara, os outros aguardam o
        // mesmo Task. WaitAsync(ct) permite cancelamento individual sem abortar
        // o Task compartilhado — próximos callers continuam sendo servidos.
        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<UserPersona>>(
            () => ResolveFromL2orL3Async(userId, userType, key),
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Race benigno: o primeiro caller a terminar remove a entry; callers
            // que JÁ pegaram o mesmo Lazy continuam sendo servidos pelo task em
            // andamento. Um caller NOVO que chegar após o TryRemove e antes do
            // task publicar o resultado criará um Lazy novo — em teoria, segunda
            // chamada HTTP. Na prática isso é inócuo porque o próprio factory
            // popula L1+L2 ANTES de retornar, então o caller novo encontra no
            // L1 (ou L2) e nunca chega ao GetOrAdd. Mantido simples deliberadamente.
            _inFlight.TryRemove(key, out _);
        }
    }

    /// <summary>
    /// Caminho de resolução (L2 → L3) executado pelo Lazy do single-flight.
    /// Não aceita CancellationToken — timeout é do HttpClient do inner provider
    /// (3s por default), pra não propagar cancelamento de um caller pros outros.
    /// </summary>
    private async Task<UserPersona> ResolveFromL2orL3Async(
        string userId, string userType, string key)
    {
        var now = DateTime.UtcNow;

        // L2: Redis
        try
        {
            var cached = await _redis.GetStringAsync(RedisKeyPrefix + key).ConfigureAwait(false);
            if (cached is not null)
            {
                var persona = Deserialize(cached, userType);
                if (persona is not null)
                {
                    StoreLocal(key, persona, now);
                    return persona;
                }
            }
        }
        catch (JsonException ex)
        {
            // Payload corrompido no Redis — loga warning e trata como miss.
            _logger.LogWarning(ex,
                "[PersonaCache] L2 payload inválido para key={Key}; refetch da fonte.", key);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            // Redis indisponível/transient — degrada pra L3 silenciosamente.
            _logger.LogDebug(ex,
                "[PersonaCache] L2 read falhou para key={Key}; tentando L3.", key);
        }

        // L3: HttpPersonaProvider — contratualmente nunca lança.
        var fresh = await _inner.ResolveAsync(userId, userType, CancellationToken.None)
            .ConfigureAwait(false);

        // Populate L2 + L1. Falha no write do Redis é best-effort.
        try
        {
            var json = Serialize(fresh);
            await _redis.SetStringAsync(
                RedisKeyPrefix + key,
                json,
                TimeSpan.FromMinutes(_options.CacheTtlMinutes)).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            _logger.LogDebug(ex,
                "[PersonaCache] L2 write falhou para key={Key}; seguindo só com L1.", key);
        }

        StoreLocal(key, fresh, now);
        return fresh;
    }

    /// <summary>
    /// Remove entrada do cache (L1 + L2). Usado pelo endpoint admin após
    /// mudanças no CRM externo ou solicitação LGPD.
    /// </summary>
    public async Task InvalidateAsync(string userId, string userType)
    {
        var key = CompositeKey(userId, userType);
        _local.TryRemove(key, out _);
        _inFlight.TryRemove(key, out _);
        try { await _redis.RemoveAsync(RedisKeyPrefix + key).ConfigureAwait(false); }
        catch (Exception ex) when (IsRedisTransient(ex))
        {
            _logger.LogDebug(ex, "[PersonaCache] Invalidate L2 falhou para key={Key}.", key);
        }

        // Cross-pod: broadcast da invalidação. Outros pods limpam L1 deles.
        await _invalidationBus.PublishInvalidateAsync(CacheName, key).ConfigureAwait(false);
    }

    private void StoreLocal(string key, UserPersona persona, DateTime now)
    {
        var expires = now + TimeSpan.FromSeconds(_options.LocalCacheTtlSeconds);
        _local[key] = new CacheEntry(persona, expires);
    }

    /// <summary>
    /// Remove entries expiradas do L1. Roda em background via <see cref="_sweepTimer"/>.
    /// O(N) sobre o dict; aceitável dado que sweep é raro (30s) e o dict é bounded
    /// por usuários ativos em ≤ 60s (TTL default).
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
            _logger.LogDebug("[PersonaCache] Sweep removeu {Removed} entries expiradas (size={Size}).",
                removed, _local.Count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sweepTimer.Dispose();
        _invalidationSubscription.Dispose();
    }

    // Redis (StackExchange.Redis) não tem exceção base próxima — usamos um
    // catch-anything controlado só para SEUS erros de rede/timeout. JsonException
    // fica FORA dessa whitelist pra não engolir payload corrompido silenciosamente.
    private static bool IsRedisTransient(Exception ex) =>
        ex is not JsonException && ex is not OperationCanceledException;

    private static string CompositeKey(string userId, string userType)
        => $"{userType}:{userId}";

    private static string Serialize(UserPersona persona) => persona switch
    {
        ClientPersona c => JsonSerializer.Serialize(c, JsonOpts),
        AdminPersona a => JsonSerializer.Serialize(a, JsonOpts),
        _ => "{}",
    };

    private static UserPersona? Deserialize(string json, string userType) => userType switch
    {
        UserPersonaFactory.ClienteUserType => JsonSerializer.Deserialize<ClientPersona>(json, JsonOpts),
        UserPersonaFactory.AdminUserType => JsonSerializer.Deserialize<AdminPersona>(json, JsonOpts),
        _ => null,
    };

    private sealed record CacheEntry(UserPersona Value, DateTime ExpiresAt);
}
