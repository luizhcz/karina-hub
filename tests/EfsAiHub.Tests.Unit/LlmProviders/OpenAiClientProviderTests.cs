using EfsAiHub.Core.Abstractions.Secrets;
using EfsAiHub.Infra.LlmProviders.Configuration;
using EfsAiHub.Infra.LlmProviders.Providers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Tests.Unit.LlmProviders;

[Trait("Category", "Unit")]
public class OpenAiClientProviderTests
{
    private sealed class PassthroughResolver : ISecretResolver
    {
        public Task<string?> ResolveAsync(string? referenceOrLiteral, SecretContext context, CancellationToken ct = default)
            => Task.FromResult(string.IsNullOrWhiteSpace(referenceOrLiteral) ? null : referenceOrLiteral);
    }

    private static OpenAiClientProvider Build(string? apiKey = "sk-test-key", string defaultModel = "gpt-4o")
    {
        var options = Options.Create(new OpenAIOptions { ApiKey = apiKey, DefaultModel = defaultModel });
        return new OpenAiClientProvider(options, new PassthroughResolver());
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

    [Fact]
    public void ProviderType_EhOPENAI()
    {
        Build().ProviderType.Should().Be("OPENAI");
    }

    [Fact]
    public async Task CreateChatClientAsync_ApiKeyGlobal_NaoLanca()
    {
        var act = async () => await Build(apiKey: "sk-global-key").CreateChatClientAsync(MakeDefinition());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateChatClientAsync_ApiKeyAusente_LancaInvalidOperationException()
    {
        var provider = Build(apiKey: null);

        var act = async () => await provider.CreateChatClientAsync(MakeDefinition(providerApiKey: null));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*OpenAI*");
    }

    [Fact]
    public async Task CreateChatClientAsync_DefinitionApiKeySobreescreveGlobal_NaoLanca()
    {
        var act = async () => await Build(apiKey: null).CreateChatClientAsync(MakeDefinition(providerApiKey: "sk-def-key"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateChatClientAsync_MesmaApiKey_AmbosSucedem()
    {
        var provider = Build(apiKey: "sk-same");
        var client1 = await provider.CreateChatClientAsync(MakeDefinition("agent-1"));
        var client2 = await provider.CreateChatClientAsync(MakeDefinition("agent-2"));

        client1.Should().NotBeNull();
        client2.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateChatClientAsync_ApiKeysDiferentes_RetornaInstanciasDiferentes()
    {
        var provider = Build(apiKey: "sk-fallback");
        var clientA = await provider.CreateChatClientAsync(MakeDefinition("a", providerApiKey: "sk-key-a"));
        var clientB = await provider.CreateChatClientAsync(MakeDefinition("b", providerApiKey: "sk-key-b"));

        clientA.Should().NotBeSameAs(clientB);
    }

    [Fact]
    public async Task CreateChatClientAsync_DeploymentNaDefinicao_NaoLanca()
    {
        var act = async () => await Build().CreateChatClientAsync(MakeDefinition(deploymentName: "gpt-4o-mini"));

        await act.Should().NotThrowAsync();
    }

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
