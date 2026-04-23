using System.Collections.Concurrent;
using System.Text.Json;
using EfsAiHub.Core.Abstractions.Identity.Persona;
using EfsAiHub.Infra.LlmProviders.Personas;
using EfsAiHub.Infra.LlmProviders.Personas.Options;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Platform.Runtime.Execution;

/// <summary>
/// Decorator de cache para <see cref="IPersonaProvider"/>:
///  - L1: dicionário in-memory local por processo (hot path, sem I/O).
///  - L2: Redis (compartilhado cross-pod, TTL configurável).
///  - L3: <see cref="HttpPersonaProvider"/> (API externa) — que JÁ nunca lança.
///
/// Responsabilidade ÚNICA: caching. Política de recovery mora no provider HTTP
/// a jusante. O decorator não engole exceções — se o provider contratualmente
/// nunca lança, não precisa try/catch aqui.
///
/// Polimorfismo na deserialização: a chave Redis já carrega o <c>userType</c>,
/// então escolhemos o subtipo concreto (<see cref="ClientPersona"/> vs
/// <see cref="AdminPersona"/>) baseado nele — evita precisar de
/// <c>[JsonDerivedType]</c> na base abstrata.
/// </summary>
public sealed class CachedPersonaProvider : IPersonaProvider
{
    private const string RedisKeyPrefix = "persona:";

    private readonly HttpPersonaProvider _inner;
    private readonly IEfsRedisCache _redis;
    private readonly PersonaApiOptions _options;
    private readonly ILogger<CachedPersonaProvider> _logger;

    private readonly ConcurrentDictionary<string, (UserPersona Value, DateTime ExpiresAt)> _local =
        new(StringComparer.Ordinal);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public CachedPersonaProvider(
        HttpPersonaProvider inner,
        IEfsRedisCache redis,
        IOptions<PersonaApiOptions> options,
        ILogger<CachedPersonaProvider> logger)
    {
        _inner = inner;
        _redis = redis;
        _options = options.Value;
        _logger = logger;
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

        // L1: local in-memory (zero I/O)
        if (_local.TryGetValue(key, out var entry) && entry.ExpiresAt > now)
            return entry.Value;

        // L2: Redis
        try
        {
            var cached = await _redis.GetStringAsync(RedisKeyPrefix + key);
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
        catch (Exception ex)
        {
            // Redis indisponível é aceitável — degrada pra API direta.
            _logger.LogDebug(ex,
                "[PersonaCache] L2 (Redis) read falhou para key={Key}; tentando L3.", key);
        }

        // L3: HttpPersonaProvider — contratualmente nunca lança.
        var fresh = await _inner.ResolveAsync(userId, userType, ct).ConfigureAwait(false);

        // Populate L2 + L1. Falha em Redis é logada mas não afeta caller.
        try
        {
            var json = Serialize(fresh);
            await _redis.SetStringAsync(
                RedisKeyPrefix + key,
                json,
                TimeSpan.FromMinutes(_options.CacheTtlMinutes));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "[PersonaCache] L2 (Redis) write falhou para key={Key}. Seguindo com L1 apenas.", key);
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
        try { await _redis.RemoveAsync(RedisKeyPrefix + key); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[PersonaCache] Invalidate L2 falhou para key={Key}.", key);
        }
    }

    private void StoreLocal(string key, UserPersona persona, DateTime now)
    {
        var expires = now + TimeSpan.FromSeconds(_options.LocalCacheTtlSeconds);
        _local[key] = (persona, expires);
    }

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
}
