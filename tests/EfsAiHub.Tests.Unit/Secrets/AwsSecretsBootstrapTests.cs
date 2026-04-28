using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using EfsAiHub.Infra.Secrets.Configuration;
using Microsoft.Extensions.Configuration;

namespace EfsAiHub.Tests.Unit.Secrets;

[Trait("Category", "Unit")]
public class AwsSecretsBootstrapTests
{
    private static ConfigurationManager BuildManager(IDictionary<string, string?> initial)
    {
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(initial);
        return manager;
    }

    [Fact]
    public void EmptyBootstrap_NoOp_DoesNotCallAws()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        var manager = BuildManager(new Dictionary<string, string?>
        {
            ["Secrets:Aws:Region"] = "us-east-1"
        });

        var act = () => manager.AddAwsSecretsBootstrap(aws);

        act.Should().NotThrow();
        aws.DidNotReceive().GetSecretValueAsync(
            Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AwsReference_ResolvesAndPopulatesConfiguration()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var req = ci.Arg<GetSecretValueRequest>();
                return Task.FromResult(new GetSecretValueResponse
                {
                    SecretString = $"resolved-{req.SecretId}"
                });
            });

        var manager = BuildManager(new Dictionary<string, string?>
        {
            ["Secrets:Aws:Region"] = "us-east-1",
            ["Secrets:Bootstrap:OpenAI:ApiKey"] = "secret://aws/efs-openai",
            ["Secrets:Bootstrap:AzureAI:ApiKey"] = "secret://aws/efs-azureai"
        });

        manager.AddAwsSecretsBootstrap(aws);

        manager["OpenAI:ApiKey"].Should().Be("resolved-efs-openai");
        manager["AzureAI:ApiKey"].Should().Be("resolved-efs-azureai");
    }

    [Fact]
    public void LiteralReference_Throws_ApontandoConfigKey()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        var manager = BuildManager(new Dictionary<string, string?>
        {
            ["Secrets:Aws:Region"] = "us-east-1",
            ["Secrets:Bootstrap:OpenAI:ApiKey"] = "sk-literal-not-allowed"
        });

        var act = () => manager.AddAwsSecretsBootstrap(aws);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OpenAI:ApiKey*")
            .WithMessage("*secret://aws/*");
    }

    [Fact]
    public void AwsCallFails_ThrowsWithContextualMessage()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        aws.GetSecretValueAsync(Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<GetSecretValueResponse>>(_ => throw new ResourceNotFoundException("missing"));

        var manager = BuildManager(new Dictionary<string, string?>
        {
            ["Secrets:Aws:Region"] = "us-east-1",
            ["Secrets:Bootstrap:OpenAI:ApiKey"] = "secret://aws/does-not-exist"
        });

        var act = () => manager.AddAwsSecretsBootstrap(aws);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OpenAI:ApiKey*")
            .WithMessage("*does-not-exist*")
            .WithInnerException<ResourceNotFoundException>();
    }

    [Fact]
    public void EmptyValuesInBootstrap_AreSkipped()
    {
        var aws = Substitute.For<IAmazonSecretsManager>();
        var manager = BuildManager(new Dictionary<string, string?>
        {
            ["Secrets:Aws:Region"] = "us-east-1",
            ["Secrets:Bootstrap:OpenAI:ApiKey"] = "",
            ["Secrets:Bootstrap:AzureAI:ApiKey"] = "   "
        });

        var act = () => manager.AddAwsSecretsBootstrap(aws);

        act.Should().NotThrow();
        aws.DidNotReceive().GetSecretValueAsync(
            Arg.Any<GetSecretValueRequest>(), Arg.Any<CancellationToken>());
    }
}
