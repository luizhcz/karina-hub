using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EfsAiHub.Infra.Persistence.Cache;

/// <summary>
/// Wrapper de cache Redis que aplica AUTOMATICAMENTE o prefixo configurado
/// (default: "efs-ai-hub:") a toda chave. Nenhum código chamador deve
/// acessar IConnectionMultiplexer / IDatabase diretamente — passe sempre
/// por este wrapper.
/// </summary>
public interface IEfsRedisCache
{
    /// <summary>Retorna a chave já com o prefixo aplicado (para uso em comandos raw como Lua).</summary>
    string BuildKey(string key);

    Task<string?> GetStringAsync(string key);
    Task SetStringAsync(string key, string value, TimeSpan? ttl = null);

    /// <summary>Retorna true se a chave existe (considerando o prefixo).</summary>
    Task<bool> ExistsAsync(string key);

    /// <summary>Atualiza o valor APENAS se a chave já existir (cache write-through respeitando regra "atualiza se está no Redis").</summary>
    Task<bool> SetIfExistsAsync(string key, string value, TimeSpan? ttl = null);

    Task<bool> RemoveAsync(string key);

    IDatabase Database { get; }
}

public sealed class EfsRedisCache : IEfsRedisCache
{
    private readonly IConnectionMultiplexer _mux;
    private readonly string _prefix;
    private readonly ILogger<EfsRedisCache> _logger;

    public EfsRedisCache(IConnectionMultiplexer mux, string prefix, ILogger<EfsRedisCache> logger)
    {
        _mux = mux;
        _prefix = string.IsNullOrWhiteSpace(prefix) ? "efs-ai-hub:" : prefix;
        _logger = logger;
    }

    public IDatabase Database => _mux.GetDatabase();

    public string BuildKey(string key) => _prefix + key;

    public async Task<string?> GetStringAsync(string key)
    {
        try { return await Database.StringGetAsync(BuildKey(key)); }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "[Redis] GET falhou para '{Key}'.", key);
            return null;
        }
    }

    public async Task SetStringAsync(string key, string value, TimeSpan? ttl = null)
    {
        try { await Database.StringSetAsync(BuildKey(key), value, ttl.HasValue ? new Expiration(ttl.Value) : Expiration.Default); }
        catch (RedisException ex) { _logger.LogWarning(ex, "[Redis] SET falhou para '{Key}'.", key); }
    }

    public async Task<bool> ExistsAsync(string key)
    {
        try { return await Database.KeyExistsAsync(BuildKey(key)); }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "[Redis] EXISTS falhou para '{Key}'.", key);
            return false;
        }
    }

    public async Task<bool> SetIfExistsAsync(string key, string value, TimeSpan? ttl = null)
    {
        try { return await Database.StringSetAsync(BuildKey(key), value, ttl, When.Exists); }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "[Redis] SET IF EXISTS falhou para '{Key}'.", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        try { return await Database.KeyDeleteAsync(BuildKey(key)); }
        catch (RedisException ex)
        {
            _logger.LogWarning(ex, "[Redis] REMOVE falhou para '{Key}'.", key);
            return false;
        }
    }
}
