using Azure.Core;
using EfsAiHub.Infra.LlmProviders.Configuration;
using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.LlmProviders;

[Trait("Category", "Unit")]
public class AzureOpenAiClientProviderTests
{
    private const string ValidEndpoint = "https://my-resource.openai.azure.com";

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
        return new AzureOpenAiClientProvider(options, credential ?? Substitute.For<TokenCredential>());
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

    // ── ProviderType ───────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_EhAZUREOPENAI()
    {
        Build().ProviderType.Should().Be("AZUREOPENAI");
    }

    // ── ApiKey path ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_ComApiKey_NaoLanca()
    {
        var act = () => Build().CreateChatClient(MakeDefinition(providerApiKey: "az-api-key-test"));

        act.Should().NotThrow();
    }

    // ── TokenCredential path ───────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_SemApiKey_UsaTokenCredential_NaoLanca()
    {
        var credential = Substitute.For<TokenCredential>();
        var act = () => Build(credential: credential).CreateChatClient(MakeDefinition(providerApiKey: null));

        act.Should().NotThrow();
    }

    // ── Definition ApiKey overrides global ─────────────────────────────────────

    [Fact]
    public void CreateChatClient_DefinitionApiKeySobreescreveGlobal_NaoLanca()
    {
        var act = () => Build().CreateChatClient(MakeDefinition(providerApiKey: "definition-specific-key"));

        act.Should().NotThrow();
    }

    // ── Invalid URI ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_EndpointInvalido_LancaUriFormatException()
    {
        var provider = Build(endpoint: "NOT_A_VALID_URI");
        var definition = MakeDefinition(providerApiKey: "some-key");

        var act = () => provider.CreateChatClient(definition);

        act.Should().Throw<UriFormatException>();
    }

    [Fact]
    public void CreateChatClient_EndpointInvalidoViaDefinition_LancaUriFormatException()
    {
        var definition = MakeDefinition(providerApiKey: "some-key", providerEndpoint: "NOT_VALID");

        var act = () => Build().CreateChatClient(definition);

        act.Should().Throw<UriFormatException>();
    }

    // ── Client caching ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_MesmaApiKeyEEndpoint_AmbosSucedem()
    {
        var provider = Build();
        var def1 = MakeDefinition("agent-1", providerApiKey: "same-key");
        var def2 = MakeDefinition("agent-2", providerApiKey: "same-key");

        provider.CreateChatClient(def1).Should().NotBeNull();
        provider.CreateChatClient(def2).Should().NotBeNull();
    }

    [Fact]
    public void CreateChatClient_ApiKeysDiferentes_RetornaInstanciasDiferentes()
    {
        var provider = Build();
        var clientA = provider.CreateChatClient(MakeDefinition("a", providerApiKey: "key-alpha"));
        var clientB = provider.CreateChatClient(MakeDefinition("b", providerApiKey: "key-beta"));

        clientA.Should().NotBeSameAs(clientB);
    }

    [Fact]
    public void CreateChatClient_EndpointsDiferentes_RetornaInstanciasDiferentes()
    {
        var credential = Substitute.For<TokenCredential>();
        var provider = Build(credential: credential);

        var defA = MakeDefinition("a", providerEndpoint: "https://resource-a.openai.azure.com");
        var defB = MakeDefinition("b", providerEndpoint: "https://resource-b.openai.azure.com");

        provider.CreateChatClient(defA).Should().NotBeSameAs(provider.CreateChatClient(defB));
    }

    // ── CreateAgentAsync ───────────────────────────────────────────────────────

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
