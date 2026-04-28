using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Infra.Secrets;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Secrets;

[Trait("Category", "Unit")]
public class SecretCacheServiceTests
{
    private static SecretCacheService BuildService(IEfsRedisCache redis, AwsSecretsOptions? options = null)
    {
        var opts = Options.Create(options ?? new AwsSecretsOptions
        {
            L1TtlSeconds = 60,
            L2TtlSeconds = 300,
            L1MaxEntries = 100,
            CacheKeyPrefix = "secret:"
        });
        return new SecretCacheService(redis, opts, NullLogger<SecretCacheService>.Instance);
    }

    [Fact]
    public async Task GetOrFetch_FirstCallHitsAws_PopulatesL1AndL2()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var sut = BuildService(redis);
        var fetchCount = 0;

        var (value, layer) = await sut.GetOrFetchAsync("my-id", _ =>
        {
            fetchCount++;
            return Task.FromResult<string?>("resolved-value");
        });

        value.Should().Be("resolved-value");
        layer.Should().Be(SecretCacheLayer.Aws);
        fetchCount.Should().Be(1);
        await redis.Received(1).SetStringAsync("secret:my-id", "resolved-value", Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task GetOrFetch_SecondCall_HitsL1_DoesNotCallFactory()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var sut = BuildService(redis);
        var fetchCount = 0;

        await sut.GetOrFetchAsync("my-id", _ =>
        {
            fetchCount++;
            return Task.FromResult<string?>("v1");
        });

        var (value, layer) = await sut.GetOrFetchAsync("my-id", _ =>
        {
            fetchCount++;
            return Task.FromResult<string?>("UNEXPECTED");
        });

        value.Should().Be("v1");
        layer.Should().Be(SecretCacheLayer.L1);
        fetchCount.Should().Be(1);
    }

    [Fact]
    public async Task GetOrFetch_L2Hit_PopulatesL1AndDoesNotCallFactory()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync("secret:my-id").Returns(Task.FromResult<string?>("from-l2"));
        var sut = BuildService(redis);
        var fetchCount = 0;

        var (value, layer) = await sut.GetOrFetchAsync("my-id", _ =>
        {
            fetchCount++;
            return Task.FromResult<string?>("UNEXPECTED");
        });

        value.Should().Be("from-l2");
        layer.Should().Be(SecretCacheLayer.L2);
        fetchCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrFetch_FactoryReturnsNull_ReturnsMissWithoutCaching()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var sut = BuildService(redis);

        var (value, layer) = await sut.GetOrFetchAsync("missing", _ => Task.FromResult<string?>(null));

        value.Should().BeNull();
        layer.Should().Be(SecretCacheLayer.Miss);
        await redis.DidNotReceive().SetStringAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<TimeSpan?>());
    }

    [Fact]
    public async Task Invalidate_RemovesFromBothLayers()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));
        var sut = BuildService(redis);

        await sut.GetOrFetchAsync("my-id", _ => Task.FromResult<string?>("v1"));

        await sut.InvalidateAsync("my-id");

        await redis.Received(1).RemoveAsync("secret:my-id");

        var (value, layer) = await sut.GetOrFetchAsync("my-id", _ => Task.FromResult<string?>("v2"));
        value.Should().Be("v2");
        layer.Should().Be(SecretCacheLayer.Aws);
    }

    [Fact]
    public async Task Invalidate_L2Failure_DoesNotThrow()
    {
        var redis = Substitute.For<IEfsRedisCache>();
        redis.RemoveAsync(Arg.Any<string>()).Returns<Task<bool>>(_ => throw new InvalidOperationException("boom"));
        var sut = BuildService(redis);

        var act = async () => await sut.InvalidateAsync("any");

        await act.Should().NotThrowAsync();
    }
}
