using EfsAiHub.Infra.LlmProviders.Configuration;
using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.LlmProviders;

[Trait("Category", "Unit")]
public class OpenAiClientProviderTests
{
    private static OpenAiClientProvider Build(string? apiKey = "sk-test-key", string defaultModel = "gpt-4o")
    {
        var options = Options.Create(new OpenAIOptions { ApiKey = apiKey, DefaultModel = defaultModel });
        return new OpenAiClientProvider(options);
    }

    private static AgentDefinition MakeDefinition(
        string id = "agent-1",
        string deploymentName = "gpt-4o",
        string? providerApiKey = null,
        string clientType = "ChatCompletion") => new()
    {
        Id = id,
        Name = "Test Agent",
        Model = new AgentModelConfig { DeploymentName = deploymentName },
        Provider = new AgentProviderConfig
        {
            Type = "OPENAI",
            ClientType = clientType,
            ApiKey = providerApiKey
        }
    };

    private static ChatClientAgentOptions MakeAgentOptions() => new()
    {
        Id = "test-agent",
        Name = "Test Agent"
    };

    // ── ProviderType ───────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_EhOPENAI()
    {
        Build().ProviderType.Should().Be("OPENAI");
    }

    // ── API key resolution ─────────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_ApiKeyGlobal_NaoLanca()
    {
        var act = () => Build(apiKey: "sk-global-key").CreateChatClient(MakeDefinition());

        act.Should().NotThrow();
    }

    [Fact]
    public void CreateChatClient_ApiKeyAusente_LancaInvalidOperationException()
    {
        var provider = Build(apiKey: null);

        var act = () => provider.CreateChatClient(MakeDefinition(providerApiKey: null));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*OpenAI*");
    }

    [Fact]
    public void CreateChatClient_DefinitionApiKeySobreescreveGlobal_NaoLanca()
    {
        var act = () => Build(apiKey: null).CreateChatClient(MakeDefinition(providerApiKey: "sk-def-key"));

        act.Should().NotThrow();
    }

    // ── Client caching ─────────────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_MesmaApiKey_AmbosSucedem()
    {
        var provider = Build(apiKey: "sk-same");
        var client1 = provider.CreateChatClient(MakeDefinition("agent-1"));
        var client2 = provider.CreateChatClient(MakeDefinition("agent-2"));

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
    }

    [Fact]
    public void CreateChatClient_ApiKeysDiferentes_RetornaInstanciasDiferentes()
    {
        var provider = Build(apiKey: "sk-fallback");
        var clientA = provider.CreateChatClient(MakeDefinition("a", providerApiKey: "sk-key-a"));
        var clientB = provider.CreateChatClient(MakeDefinition("b", providerApiKey: "sk-key-b"));

        clientA.Should().NotBeSameAs(clientB);
    }

    // ── Deployment resolution ──────────────────────────────────────────────────

    [Fact]
    public void CreateChatClient_DeploymentNaDefinicao_NaoLanca()
    {
        var act = () => Build().CreateChatClient(MakeDefinition(deploymentName: "gpt-4o-mini"));

        act.Should().NotThrow();
    }

    // ── CreateAgentAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAgentAsync_ChatCompletion_RetornaObjetoNaoNulo()
    {
        var provider = Build();
        var agent = await provider.CreateAgentAsync(MakeDefinition(clientType: "ChatCompletion"), MakeAgentOptions());

        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAgentAsync_ResponsesMode_RetornaObjetoNaoNulo()
    {
        var provider = Build();
        var agent = await provider.CreateAgentAsync(MakeDefinition(clientType: "RESPONSES"), MakeAgentOptions());

        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAgentAsync_ApiKeyAusente_LancaInvalidOperationException()
    {
        var provider = Build(apiKey: null);

        var act = async () => await provider.CreateAgentAsync(MakeDefinition(), MakeAgentOptions());

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
