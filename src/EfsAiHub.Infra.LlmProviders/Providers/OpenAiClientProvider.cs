using System.Collections.Concurrent;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace EfsAiHub.Infra.LlmProviders.Providers;

/// <summary>
/// Cria agentes e chat clients usando a API OpenAI diretamente.
/// Suporta modos ChatCompletion e Responses.
/// </summary>
public class OpenAiClientProvider : ILlmClientProvider
{
    private readonly OpenAIOptions _options;
    // Cache de OpenAIClient por hash da apiKey — evita TLS/HttpMessageHandler churn.
    private readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new();

    public OpenAiClientProvider(IOptions<OpenAIOptions> options) => _options = options.Value;

    private OpenAIClient GetOrCreateClient(string apiKey)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)));
        return _clientCache.GetOrAdd(hash, _ => new OpenAIClient(apiKey));
    }

    public string ProviderType => "OPENAI";

    public Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey(definition);
        var client = GetOrCreateClient(apiKey);
        var deploymentName = ResolveDeployment(definition);

        object agent = definition.Provider.ClientType.ToUpperInvariant() switch
        {
            "RESPONSES" => client.GetResponsesClient().AsIChatClient().AsAIAgent(options),
            _ => (object)client.GetChatClient(deploymentName).AsIChatClient().AsAIAgent(options)
        };

        return Task.FromResult(agent);
    }

    public IChatClient CreateChatClient(AgentDefinition definition)
    {
        var apiKey = ResolveApiKey(definition);
        var client = GetOrCreateClient(apiKey);
        return client.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
    }

    private string ResolveApiKey(AgentDefinition definition)
    {
        var apiKey = string.IsNullOrWhiteSpace(definition.Provider.ApiKey)
            ? _options.ApiKey
            : definition.Provider.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"Agent '{definition.Id}': provider OpenAI requires 'provider.apiKey' or 'OpenAI.ApiKey' in appsettings.");

        return apiKey;
    }

    private string ResolveDeployment(AgentDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.Model.DeploymentName)
            ? definition.Model.DeploymentName
            : _options.DefaultModel;
}
