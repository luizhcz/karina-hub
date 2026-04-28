using Azure.Core;
using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.LlmProviders.Configuration;
using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.LlmProviders;

[Trait("Category", "Unit")]
public class AzureOpenAiClientProviderTests
{
    private const string ValidEndpoint = "https://my-resource.openai.azure.com";

    private sealed class PassthroughResolver : ISecretResolver
    {
        public Task<string?> ResolveAsync(string? referenceOrLiteral, SecretContext context, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(referenceOrLiteral) ? null : referenceOrLiteral);
    }

    private static AzureOpenAiClientProvider Build(
        string endpoint = ValidEndpoint,
        string deployment = "gpt-4o",
        TokenCredential? credential = null)
    {
        var options = Options.Create(new AzureAIOptions
        {
            Endpoint = endpoint,
            DefaultDeploymentName = deployment
        });
        return new AzureOpenAiClientProvider(options, credential ?? Substitute.For<TokenCredential>(), new PassthroughResolver());
    }

    private static AgentDefinition MakeDefinition(
        string id = "agent-az-1",
        string deploymentName = "gpt-4o",
        string? providerApiKey = null,
        string? providerEndpoint = null) => new()
    {
        Id = id,
        Name = "Azure Test Agent",
        Model = new AgentModelConfig { DeploymentName = deploymentName },
        Provider = new AgentProviderConfig
        {
            Type = "AZUREOPENAI",
            ApiKey = providerApiKey,
            Endpoint = providerEndpoint
        }
    };

    private static ChatClientAgentOptions MakeAgentOptions() => new()
    {
        Id = "test-agent",
        Name = "Test Agent"
    };

    [Fact]
    public void ProviderType_EhAZUREOPENAI()
    {
        Build().ProviderType.Should().Be("AZUREOPENAI");
    }

    [Fact]
    public async Task CreateChatClientAsync_ComApiKey_NaoLanca()
    {
        var act = async () => await Build().CreateChatClientAsync(MakeDefinition(providerApiKey: "az-api-key-test"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateChatClientAsync_SemApiKey_UsaTokenCredential_NaoLanca()
    {
        var credential = Substitute.For<TokenCredential>();
        var act = async () => await Build(credential: credential).CreateChatClientAsync(MakeDefinition(providerApiKey: null));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateChatClientAsync_DefinitionApiKeySobreescreveGlobal_NaoLanca()
    {
        var act = async () => await Build().CreateChatClientAsync(MakeDefinition(providerApiKey: "definition-specific-key"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateChatClientAsync_EndpointInvalido_LancaUriFormatException()
    {
        var provider = Build(endpoint: "NOT_A_VALID_URI");
        var definition = MakeDefinition(providerApiKey: "some-key");

        var act = async () => await provider.CreateChatClientAsync(definition);

        await act.Should().ThrowAsync<UriFormatException>();
    }

    [Fact]
    public async Task CreateChatClientAsync_EndpointInvalidoViaDefinition_LancaUriFormatException()
    {
        var definition = MakeDefinition(providerApiKey: "some-key", providerEndpoint: "NOT_VALID");

        var act = async () => await Build().CreateChatClientAsync(definition);

        await act.Should().ThrowAsync<UriFormatException>();
    }

    [Fact]
    public async Task CreateChatClientAsync_MesmaApiKeyEEndpoint_AmbosSucedem()
    {
        var provider = Build();
        var def1 = MakeDefinition("agent-1", providerApiKey: "same-key");
        var def2 = MakeDefinition("agent-2", providerApiKey: "same-key");

        (await provider.CreateChatClientAsync(def1)).Should().NotBeNull();
        (await provider.CreateChatClientAsync(def2)).Should().NotBeNull();
    }

    [Fact]
    public async Task CreateChatClientAsync_ApiKeysDiferentes_RetornaInstanciasDiferentes()
    {
        var provider = Build();
        var clientA = await provider.CreateChatClientAsync(MakeDefinition("a", providerApiKey: "key-alpha"));
        var clientB = await provider.CreateChatClientAsync(MakeDefinition("b", providerApiKey: "key-beta"));

        clientA.Should().NotBeSameAs(clientB);
    }

    [Fact]
    public async Task CreateChatClientAsync_EndpointsDiferentes_RetornaInstanciasDiferentes()
    {
        var credential = Substitute.For<TokenCredential>();
        var provider = Build(credential: credential);

        var defA = MakeDefinition("a", providerEndpoint: "https://resource-a.openai.azure.com");
        var defB = MakeDefinition("b", providerEndpoint: "https://resource-b.openai.azure.com");

        var clientA = await provider.CreateChatClientAsync(defA);
        var clientB = await provider.CreateChatClientAsync(defB);

        clientA.Should().NotBeSameAs(clientB);
    }

    [Fact]
    public async Task CreateAgentAsync_ComApiKey_RetornaObjetoNaoNulo()
    {
        var provider = Build();
        var definition = MakeDefinition(providerApiKey: "az-key");

        var agent = await provider.CreateAgentAsync(definition, MakeAgentOptions());

        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAgentAsync_SemApiKey_UsaCredential_RetornaObjetoNaoNulo()
    {
        var credential = Substitute.For<TokenCredential>();
        var provider = Build(credential: credential);
        var definition = MakeDefinition(providerApiKey: null);

        var agent = await provider.CreateAgentAsync(definition, MakeAgentOptions());

        agent.Should().NotBeNull();
    }
}
