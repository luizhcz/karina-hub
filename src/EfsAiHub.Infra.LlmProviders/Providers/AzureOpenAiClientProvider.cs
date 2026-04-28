using System.Collections.Concurrent;
using Azure.AI.OpenAI;
using Azure.Core;
using EfsAiHub.Core.Abstractions.Secrets;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Providers;

/// <summary>
/// Cria agentes e chat clients usando Azure OpenAI (AzureOpenAIClient).
/// Suporta autenticação via TokenCredential ou ApiKey resolvida via ISecretResolver.
/// </summary>
public class AzureOpenAiClientProvider : ILlmClientProvider
{
    private readonly AzureAIOptions _options;
    private readonly TokenCredential _credential;
    private readonly ISecretResolver _secretResolver;
    // Reuse de AzureOpenAIClient por (endpoint|authKind|apiKeyHash).
    // AzureOpenAIClient é thread-safe e long-lived; instanciar por chamada explodia sockets/TLS.
    private readonly ConcurrentDictionary<string, AzureOpenAIClient> _clientCache = new();

    public AzureOpenAiClientProvider(IOptions<AzureAIOptions> options, TokenCredential credential, ISecretResolver secretResolver)
    {
        _options = options.Value;
        _credential = credential;
        _secretResolver = secretResolver;
    }

    public string ProviderType => "AZUREOPENAI";

    public async Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var client = await CreateAzureClientAsync(definition, ct);
        var deploymentName = ResolveDeployment(definition);
        var chatClient = client.GetChatClient(deploymentName).AsIChatClient();
        return chatClient.AsAIAgent(options);
    }

    public async Task<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var client = await CreateAzureClientAsync(definition, ct);
        return client.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
    }

    private async Task<AzureOpenAIClient> CreateAzureClientAsync(AgentDefinition definition, CancellationToken ct)
    {
        var endpointUri = definition.Provider.Endpoint ?? _options.Endpoint;
        var rawKey = definition.Provider.ApiKey ?? _options.ApiKey;

        var scope = ResolveScope(definition);

        var apiKey = await _secretResolver.ResolveAsync(rawKey, scope, ct);

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

    private static SecretContext ResolveScope(AgentDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Provider.ApiKey))
            return SecretContext.Global("AzureAI:ApiKey");
        if (string.IsNullOrWhiteSpace(definition.ProjectId))
            return SecretContext.Global($"azureopenai:agent:{definition.Id}");
        return SecretContext.Project(definition.ProjectId, "azureopenai", definition.Id);
    }
}
