using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Providers;

/// <summary>
/// Cria agentes e chat clients usando Azure OpenAI (AzureOpenAIClient).
/// Suporta autenticação via TokenCredential ou ApiKey.
/// </summary>
public class AzureOpenAiClientProvider : ILlmClientProvider
{
    private readonly AzureAIOptions _options;
    private readonly TokenCredential _credential;
    // Reuse de AzureOpenAIClient por (endpoint|authKind|apiKeyHash).
    // AzureOpenAIClient é thread-safe e long-lived; instanciar por chamada explodia sockets/TLS.
    private readonly ConcurrentDictionary<string, AzureOpenAIClient> _clientCache = new();

    public AzureOpenAiClientProvider(IOptions<AzureAIOptions> options, TokenCredential credential)
    {
        _options = options.Value;
        _credential = credential;
    }

    public string ProviderType => "AZUREOPENAI";

    public Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var client = CreateAzureClient(definition);
        var deploymentName = ResolveDeployment(definition);
        var chatClient = client.GetChatClient(deploymentName).AsIChatClient();
        object agent = chatClient.AsAIAgent(options);
        return Task.FromResult(agent);
    }

    public IChatClient CreateChatClient(AgentDefinition definition)
    {
        var client = CreateAzureClient(definition);
        return client.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
    }

    private AzureOpenAIClient CreateAzureClient(AgentDefinition definition)
    {
        var endpointUri = definition.Provider.Endpoint ?? _options.Endpoint;
        var apiKey = definition.Provider.ApiKey ?? _options.ApiKey;
        // A chave não inclui o segredo em claro — só um hash estável suficiente pra distinguir credenciais distintas.
        string cacheKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            cacheKey = $"tc|{endpointUri}";
        }
        else
        {
            var hash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)));
            cacheKey = $"ak|{endpointUri}|{hash}";
        }

        return _clientCache.GetOrAdd(cacheKey, _ =>
        {
            var endpoint = new Uri(endpointUri);
            return string.IsNullOrWhiteSpace(apiKey)
                ? new AzureOpenAIClient(endpoint, _credential)
                : new AzureOpenAIClient(endpoint, new System.ClientModel.ApiKeyCredential(apiKey));
        });
    }

    private string ResolveDeployment(AgentDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.Model.DeploymentName)
            ? definition.Model.DeploymentName
            : _options.DefaultDeploymentName;
}
