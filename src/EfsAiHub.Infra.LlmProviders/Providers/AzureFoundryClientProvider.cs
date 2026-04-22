using Azure.AI.Agents.Persistent;
using Azure.Core;
using EfsAiHub.Core.Agents.McpServers;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace EfsAiHub.Infra.LlmProviders.Providers;

/// <summary>
/// Cria agentes usando Azure Foundry (PersistentAgentsClient).
/// Agentes são criados server-side com managed state.
/// Depende de <see cref="IMcpServerRepository"/> para resolver em runtime os MCP tools
/// que referenciam um registro por <c>McpServerId</c> (id-based live resolution).
/// </summary>
public class AzureFoundryClientProvider : ILlmClientProvider
{
    private readonly AzureAIOptions _options;
    private readonly TokenCredential _credential;
    private readonly IMcpServerRepository _mcpRepo;
    private readonly ILogger<AzureFoundryClientProvider> _logger;

    public AzureFoundryClientProvider(
        IOptions<AzureAIOptions> options,
        TokenCredential credential,
        IMcpServerRepository mcpRepo,
        ILogger<AzureFoundryClientProvider> logger)
    {
        _options = options.Value;
        _credential = credential;
        _mcpRepo = mcpRepo;
        _logger = logger;
    }

    public string ProviderType => "AZUREFOUNDRY";

    public async Task<object> CreateAgentAsync(
        AgentDefinition definition, ChatClientAgentOptions options, CancellationToken ct = default)
    {
        var endpoint = definition.Provider.Endpoint ?? _options.Endpoint;
        var client = new PersistentAgentsClient(endpoint, _credential);

        var toolDefinitions = await FoundryToolBuilder.BuildAsync(definition, _mcpRepo, _logger, ct);
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
/// Async porque o tipo <c>mcp</c> resolve o registro em runtime via <see cref="IMcpServerRepository"/>.
/// </summary>
internal static class FoundryToolBuilder
{
    public static async Task<List<ToolDefinition>> BuildAsync(
        AgentDefinition definition,
        IMcpServerRepository mcpRepo,
        ILogger logger,
        CancellationToken ct = default)
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
                    var mcpDef = await ResolveMcpAsync(tool, mcpRepo, logger, ct);
                    if (mcpDef is not null) tools.Add(mcpDef);
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

    /// <summary>
    /// Resolve uma MCP tool em duas ordens:
    ///   1. id-based (preferido): busca registro em <c>aihub.mcp_servers</c>; se não achar,
    ///      loga warning e pula a tool (dangling — MCP foi deletado).
    ///   2. legacy/fallback: se <c>McpServerId</c> é null, usa os campos inline
    ///      (<c>ServerLabel</c>, <c>ServerUrl</c>, <c>AllowedTools</c>) do próprio
    ///      <see cref="AgentToolDefinition"/>. Mantém BC com agents seedados antes do registry.
    /// </summary>
    private static async Task<MCPToolDefinition?> ResolveMcpAsync(
        AgentToolDefinition tool,
        IMcpServerRepository mcpRepo,
        ILogger logger,
        CancellationToken ct)
    {
        string? label;
        string? url;
        IEnumerable<string> allowed;

        if (!string.IsNullOrWhiteSpace(tool.McpServerId))
        {
            var server = await mcpRepo.GetByIdAsync(tool.McpServerId, ct);
            if (server is null)
            {
                logger.LogWarning(
                    "MCP tool '{McpServerId}' não encontrado no registry — tool pulada (dangling reference).",
                    tool.McpServerId);
                return null;
            }
            label = server.ServerLabel;
            url = server.ServerUrl;
            allowed = server.AllowedTools;
        }
        else if (!string.IsNullOrWhiteSpace(tool.ServerLabel) && !string.IsNullOrWhiteSpace(tool.ServerUrl))
        {
            label = tool.ServerLabel;
            url = tool.ServerUrl;
            allowed = tool.AllowedTools;
        }
        else
        {
            logger.LogWarning("MCP tool ignored: requires 'McpServerId' or inline 'ServerLabel'+'ServerUrl'.");
            return null;
        }

        var mcpTool = new MCPToolDefinition(label!, url!);
        foreach (var name in allowed)
            mcpTool.AllowedTools.Add(name);
        return mcpTool;
    }
}
