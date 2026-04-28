using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.Persistence.Cache;
using EfsAiHub.Infra.Secrets;
using EfsAiHub.Infra.Secrets.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.Secrets;

[Trait("Category", "Unit")]
public class AwsSecretsManagerResolverTests
{
    private static (AwsSecretsManagerResolver Resolver, IAmazonSecretsManager Aws, IEfsRedisCache Redis)
        Build()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        var redis = Substitute.For<IEfsRedisCache>();
        redis.GetStringAsync(Arg.Any<string>()).Returns(Task.FromResult<string?>(null));

        var opts = Options.Create(new AwsSecretsOptions
        {
            L1TtlSeconds = 60,
            L2TtlSeconds = 300,
            L1MaxEntries = 100,
            CacheKeyPrefix = "secret:"
        });

        var cache = new SecretCacheService(redis, opts, NullLogger<SecretCacheService>.Instance);
        var resolver = new AwsSecretsManagerResolver(aws, cache, NullLogger<AwsSecretsManagerResolver>.Instance);
        return (resolver, aws, redis);
    }

    [Fact]
    public async Task Resolve_Null_ReturnsNull()
    {
        var (resolver, _, _) = Build();
        var result = await resolver.ResolveAsync(null, SecretContext.Global());
        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_Empty_ReturnsNull()
    {
        var (resolver, _, _) = Build();
        var result = await resolver.ResolveAsync("   ", SecretContext.Global());
        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_Literal_ReturnsLiteralWithoutAwsCall()
    {
        var (resolver, aws, _) = Build();

        var result = await resolver.ResolveAsync("sk-literal-key", SecretContext.Project("proj-1", "openai"));

        result.Should().Be("sk-literal-key");
        await aws.DidNotReceive().GetSecretValueAsync(
            Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_AwsReference_ReturnsResolvedValue()
    {
        var (resolver, aws, _) = Build();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetSecretValueResponse { SecretString = "resolved" }));

        var result = await resolver.ResolveAsync("secret://aws/efs-foo", SecretContext.Global());

        result.Should().Be("resolved");
        await aws.Received(1).GetSecretValueAsync(
            Arg.Is<GetSecretValueRequest>(r => r.SecretId == "efs-foo"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_AwsReference_SecondCall_HitsCache()
    {
        var (resolver, aws, _) = Build();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GetSecretValueResponse { SecretString = "cached" }));

        await resolver.ResolveAsync("secret://aws/efs-foo", SecretContext.Global());
        var secondCall = await resolver.ResolveAsync("secret://aws/efs-foo", SecretContext.Global());

        secondCall.Should().Be("cached");
        await aws.Received(1).GetSecretValueAsync(
            Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_AwsReferenceNotFound_ReturnsNull()
    {
        var (resolver, aws, _) = Build();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetSecretValueResponse>>(_ => throw new ResourceNotFoundException("missing"));

        var result = await resolver.ResolveAsync("secret://aws/does-not-exist", SecretContext.Global());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resolve_AwsTransientFailure_Throws()
    {
        var (resolver, aws, _) = Build();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetSecretValueResponse>>(_ => throw new InternalServiceErrorException("aws-down"));

        var act = async () => await resolver.ResolveAsync("secret://aws/foo", SecretContext.Global());

        await act.Should().ThrowAsync<InternalServiceErrorException>();
    }
}
