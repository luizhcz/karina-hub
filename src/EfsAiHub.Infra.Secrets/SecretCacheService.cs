using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.Secrets;

public interface ISecretCacheService
{
    Task<(string? Value, SecretCacheLayer Layer)> GetOrFetchAsync(
        string identifier,
        Func<CancellationToken, Task<string?>> factory,
        CancellationToken ct = default);

    Task InvalidateAsync(string identifier);
}

public enum SecretCacheLayer
{
    L1,
    L2,
    Aws,
    Miss
}

public sealed class SecretCacheService : ISecretCacheService, IDisposable
{
    private readonly IMemoryCache _l1;
    private readonly IEfsRedisCache _l2;
    private readonly AwsSecretsOptions _options;
    private readonly ILogger<SecretCacheService> _logger;

    public SecretCacheService(
        IEfsRedisCache l2,
        IOptions<AwsSecretsOptions> options,
        ILogger<SecretCacheService> logger)
    {
        _l2 = l2;
        _options = options.Value;
        _logger = logger;
        _l1 = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = _options.L1MaxEntries
        });
    }

    public async Task<(string? Value, SecretCacheLayer Layer)> GetOrFetchAsync(
        string identifier,
        Func<CancellationToken, Task<string?>> factory,
        CancellationToken ct = default)
    {
        if (_l1.TryGetValue<string>(identifier, out var l1Value) && l1Value is not null)
            return (l1Value, SecretCacheLayer.L1);

        var l2Key = _options.CacheKeyPrefix + identifier;
        var l2Value = await _l2.GetStringAsync(l2Key);
        if (!string.IsNullOrEmpty(l2Value))
        {
            PopulateL1(identifier, l2Value);
            return (l2Value, SecretCacheLayer.L2);
        }

        var fetched = await factory(ct);
        if (string.IsNullOrEmpty(fetched))
            return (null, SecretCacheLayer.Miss);

        await PopulateBothAsync(identifier, fetched);
        return (fetched, SecretCacheLayer.Aws);
    }

    public async Task InvalidateAsync(string identifier)
    {
        _l1.Remove(identifier);
        var l2Key = _options.CacheKeyPrefix + identifier;
        try
        {
            await _l2.RemoveAsync(l2Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SecretCache] Falha ao invalidar L2 '{Identifier}'.", identifier);
        }
    }

    private void PopulateL1(string identifier, string value)
    {
        using var entry = _l1.CreateEntry(identifier);
        entry.Value = value;
        entry.Size = 1;
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.L1TtlSeconds);
    }

    private async Task PopulateBothAsync(string identifier, string value)
    {
        PopulateL1(identifier, value);
        var l2Key = _options.CacheKeyPrefix + identifier;
        try
        {
            await _l2.SetStringAsync(l2Key, value, TimeSpan.FromSeconds(_options.L2TtlSeconds));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SecretCache] Falha ao popular L2 '{Identifier}'.", identifier);
        }
    }

    public void Dispose() => _l1.Dispose();
}
