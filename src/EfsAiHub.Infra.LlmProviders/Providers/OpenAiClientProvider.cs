using System.Collections.Concurrent;
using EfsAiHub.Core.Abstractions.Secrets;
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
    private readonly ISecretResolver _secretResolver;
    private readonly ConcurrentDictionary<string, OpenAIClient> _clientCache = new();

    public OpenAiClientProvider(IOptions<OpenAIOptions> options, ISecretResolver secretResolver)
    {
        _options = options.Value;
        _secretResolver = secretResolver;
    }

    private OpenAIClient GetOrCreateClient(string apiKey)
    {
        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)));
        return _clientCache.GetOrAdd(hash, _ => new OpenAIClient(apiKey));
    }

    public string ProviderType => "OPENAI";

    public async Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var apiKey = await ResolveApiKeyAsync(definition, ct);
        var client = GetOrCreateClient(apiKey);
        var deploymentName = ResolveDeployment(definition);

        return definition.Provider.ClientType.ToUpperInvariant() switch
        {
            "RESPONSES" => client.GetResponsesClient().AsIChatClient().AsAIAgent(options),
            _           => (object)client.GetChatClient(deploymentName).AsIChatClient().AsAIAgent(options)
        };
    }

    public async Task<IChatClient> CreateChatClientAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var apiKey = await ResolveApiKeyAsync(definition, ct);
        var client = GetOrCreateClient(apiKey);
        return client.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
    }

    private async Task<string> ResolveApiKeyAsync(AgentDefinition definition, CancellationToken ct)
    {
        var rawKey = string.IsNullOrWhiteSpace(definition.Provider.ApiKey)
            ? _options.ApiKey
            : definition.Provider.ApiKey;

        var scope = ResolveScope(definition, "openai", "OpenAI:ApiKey");

        var apiKey = await _secretResolver.ResolveAsync(rawKey, scope, ct);

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                $"Agent '{definition.Id}': provider OpenAI requires 'provider.apiKey' or 'OpenAI.ApiKey' in appsettings.");

        return apiKey;
    }

    private string ResolveDeployment(AgentDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.Model.DeploymentName)
            ? definition.Model.DeploymentName
            : _options.DefaultModel;

    private static SecretContext ResolveScope(AgentDefinition definition, string provider, string globalLabel)
    {
        if (string.IsNullOrWhiteSpace(definition.Provider.ApiKey))
            return SecretContext.Global(globalLabel);
        if (string.IsNullOrWhiteSpace(definition.ProjectId))
            return SecretContext.Global($"{provider}:agent:{definition.Id}");
        return SecretContext.Project(definition.ProjectId, provider, definition.Id);
    }
}
