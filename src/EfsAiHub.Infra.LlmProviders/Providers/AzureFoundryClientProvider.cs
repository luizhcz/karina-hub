using Azure.AI.Agents.Persistent;
using Azure.Core;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Providers;

/// <summary>
/// Cria agentes usando Azure Foundry (PersistentAgentsClient).
/// Agentes são criados server-side com managed state.
/// </summary>
public class AzureFoundryClientProvider : ILlmClientProvider
{
    private readonly AzureAIOptions _options;
    private readonly TokenCredential _credential;
    private readonly ILogger<AzureFoundryClientProvider> _logger;

    public AzureFoundryClientProvider(
        IOptions<AzureAIOptions> options,
        TokenCredential credential,
        ILogger<AzureFoundryClientProvider> logger)
    {
        _options = options.Value;
        _credential = credential;
        _logger = logger;
    }

    public string ProviderType => "AZUREFOUNDRY";

    public async Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var endpoint = definition.Provider.Endpoint ?? _options.Endpoint;
        var client = new PersistentAgentsClient(endpoint, _credential);

        var toolDefinitions = FoundryToolBuilder.Build(definition, _logger);
        var deploymentName = ResolveDeployment(definition);

        var agentResult = await client.Administration.CreateAgentAsync(
            model: deploymentName,
            name: definition.Name,
            instructions: definition.Instructions,
            tools: toolDefinitions,
            cancellationToken: ct);

        _logger.LogInformation("Agent '{AgentId}' created on Azure Foundry with remote ID: {RemoteId}",
            definition.Id, agentResult.Value.Id);

        var chatClient = client.AsIChatClient(agentResult.Value.Id, null, true);
        return chatClient.AsAIAgent(options);
    }

    public IChatClient CreateChatClient(AgentDefinition definition)
    {
        // Foundry no modo Graph usa a mesma API compatível com OpenAI
        var apiKey = definition.Provider.ApiKey;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            var client = new OpenAI.OpenAIClient(apiKey);
            return client.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
        }
        // Fallback: cliente Azure OpenAI com credencial
        var endpoint = new Uri(definition.Provider.Endpoint ?? _options.Endpoint);
        var azureClient = new Azure.AI.OpenAI.AzureOpenAIClient(endpoint, _credential);
        return azureClient.GetChatClient(ResolveDeployment(definition)).AsIChatClient();
    }

    private string ResolveDeployment(AgentDefinition definition) =>
        !string.IsNullOrWhiteSpace(definition.Model.DeploymentName)
            ? definition.Model.DeploymentName
            : _options.DefaultDeploymentName;
}

/// <summary>
/// Constrói a lista de ToolDefinition para o PersistentAgentsClient do Azure Foundry.
/// </summary>
internal static class FoundryToolBuilder
{
    public static List<ToolDefinition> Build(AgentDefinition definition, ILogger logger)
    {
        var tools = new List<ToolDefinition>();

        foreach (var tool in definition.Tools)
        {
            switch (tool.Type.ToLowerInvariant())
            {
                case "code_interpreter":
                    tools.Add(new CodeInterpreterToolDefinition());
                    break;

                case "file_search":
                    tools.Add(new FileSearchToolDefinition());
                    break;

                case "web_search":
                    if (string.IsNullOrWhiteSpace(tool.ConnectionId))
                    {
                        logger.LogWarning("web_search tool ignored: connectionId is required for Bing Grounding on Foundry.");
                        break;
                    }
                    var bingConfig = new BingGroundingSearchConfiguration(tool.ConnectionId);
                    var bingParams = new BingGroundingSearchToolParameters([bingConfig]);
                    tools.Add(new BingGroundingToolDefinition(bingParams));
                    break;

                case "mcp":
                    if (string.IsNullOrWhiteSpace(tool.ServerLabel) || string.IsNullOrWhiteSpace(tool.ServerUrl))
                    {
                        logger.LogWarning("MCP tool ignored: serverLabel and serverUrl are required.");
                        break;
                    }
                    var mcpTool = new MCPToolDefinition(tool.ServerLabel, tool.ServerUrl);
                    foreach (var allowed in tool.AllowedTools)
                        mcpTool.AllowedTools.Add(allowed);
                    tools.Add(mcpTool);
                    break;

                case "function":
                    // Function tools são aplicados em runtime via IFunctionToolRegistry.
                    break;

                default:
                    logger.LogWarning("Unknown tool type: '{ToolType}' — ignored.", tool.Type);
                    break;
            }
        }

        return tools;
    }
}
