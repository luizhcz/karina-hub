using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace EfsAiHub.Platform.Runtime.Factories;

/// <summary>
/// Centraliza a construção de ChatClientAgentOptions e ChatOptions
/// a partir de um AgentDefinition. Resolve function tools do registry,
/// envolve com TrackedAIFunction para rastreamento de invocações e aplica
/// configurações de modelo e structured output.
/// </summary>
public static class ChatOptionsBuilder
{
    /// <summary>
    /// Constrói ChatClientAgentOptions para uso com IChatClient.AsAIAgent().
    /// Inclui identidade do agente, instruções, parâmetros de modelo, tools e structured output.
    /// </summary>
    public static ChatClientAgentOptions BuildAgentOptions(
        AgentDefinition definition,
        IFunctionToolRegistry functionRegistry,
        ChannelWriter<ToolInvocation> toolWriter,
        ILogger<TrackedAIFunction> trackedFnLogger,
        ILogger logger,
        bool allowFingerprintMismatch = true,
        string? projectId = null)
    {
        var chatOptions = BuildCoreOptions(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId);

        return new ChatClientAgentOptions
        {
            Id = definition.Id,
            Name = definition.Name,
            Description = definition.Description,
            ChatOptions = chatOptions
        };
    }

    /// <summary>
    /// Constrói ChatOptions para uso com IChatClient.GetResponseAsync() no modo Graph.
    /// Similar ao BuildAgentOptions mas retorna ChatOptions puro (sem wrapper de identidade do agente).
    /// </summary>
    public static ChatOptions BuildGraphChatOptions(
        AgentDefinition definition,
        IFunctionToolRegistry functionRegistry,
        ChannelWriter<ToolInvocation> toolWriter,
        ILogger<TrackedAIFunction> trackedFnLogger,
        ILogger logger,
        bool allowFingerprintMismatch = true,
        string? projectId = null)
    {
        return BuildCoreOptions(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId);
    }

    private static ChatOptions BuildCoreOptions(
        AgentDefinition definition,
        IFunctionToolRegistry functionRegistry,
        ChannelWriter<ToolInvocation> toolWriter,
        ILogger<TrackedAIFunction> trackedFnLogger,
        ILogger logger,
        bool allowFingerprintMismatch,
        string? projectId)
    {
        var options = new ChatOptions
        {
            Instructions = definition.Instructions,
            Temperature = definition.Model.Temperature,
            MaxOutputTokens = definition.Model.MaxTokens,
            ModelId = definition.Model.DeploymentName
        };

        var tools = BuildFunctionTools(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId);
        if (tools.Count > 0)
            options.Tools = tools;

        var responseFormat = BuildResponseFormat(definition.StructuredOutput, logger, definition.Id);
        if (responseFormat is not null)
            options.ResponseFormat = responseFormat;

        return options;
    }

    private static List<AITool> BuildFunctionTools(
        AgentDefinition definition,
        IFunctionToolRegistry functionRegistry,
        ChannelWriter<ToolInvocation> toolWriter,
        ILogger<TrackedAIFunction> trackedFnLogger,
        ILogger logger,
        bool allowFingerprintMismatch,
        string? projectId)
    {
        var tools = new List<AITool>();
        var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Resolve function tools explícitos do registry (por fingerprint quando disponível).
        foreach (var toolDef in definition.Tools.Where(t =>
            t.Type.Equals("function", StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(toolDef.Name))
            {
                logger.LogWarning("Agent '{AgentId}': function tool without name — ignored.", definition.Id);
                continue;
            }

            var fn = ResolveByFingerprintOrLatest(
                definition.Id, toolDef.Name!, toolDef.FingerprintHash,
                functionRegistry, logger, allowFingerprintMismatch, projectId);
            if (fn is null)
            {
                logger.LogWarning("Agent '{AgentId}': function '{ToolName}' not found in registry — ignored.",
                    definition.Id, toolDef.Name);
                continue;
            }

            tools.Add(new TrackedAIFunction(fn, definition.Id, toolWriter, trackedFnLogger));
            addedNames.Add(toolDef.Name!);
        }

        // Resolve MCP tools: se uma entrada AllowedTools existe no FunctionToolRegistry,
        // usa a implementação registrada como fallback (evita precisar de um cliente MCP em runtime).
        foreach (var toolDef in definition.Tools.Where(t =>
            t.Type.Equals("mcp", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var mcpToolName in toolDef.AllowedTools)
            {
                if (addedNames.Contains(mcpToolName))
                    continue;

                var found = projectId is not null
                    ? functionRegistry.TryGet(mcpToolName, projectId, out var fn)
                    : functionRegistry.TryGet(mcpToolName, out fn);
                if (!found || fn is null)
                {
                    logger.LogWarning(
                        "Agent '{AgentId}': MCP tool '{ToolName}' (server '{ServerLabel}') not found in FunctionToolRegistry — ignored. " +
                        "Register it as a function tool or implement MCP client resolution.",
                        definition.Id, mcpToolName, toolDef.ServerLabel);
                    continue;
                }

                logger.LogDebug(
                    "Agent '{AgentId}': MCP tool '{ToolName}' resolved from FunctionToolRegistry (fallback).",
                    definition.Id, mcpToolName);
                tools.Add(new TrackedAIFunction(fn, definition.Id, toolWriter, trackedFnLogger));
                addedNames.Add(mcpToolName);
            }
        }

        return tools;
    }

    /// <summary>
    /// Resolve a tool pelo fingerprint snapshoteado (se presente), falhando
    /// ou caindo para <c>GetLatest</c> conforme feature flag.
    /// </summary>
    private static AIFunction? ResolveByFingerprintOrLatest(
        string agentId,
        string toolName,
        string? expectedFingerprint,
        IFunctionToolRegistry registry,
        ILogger logger,
        bool allowMismatch,
        string? projectId)
    {
        if (!string.IsNullOrEmpty(expectedFingerprint))
        {
            var byFp = registry.GetByFingerprint(toolName, expectedFingerprint);
            if (byFp is not null) return byFp;

            if (!allowMismatch)
                throw new ToolFingerprintMismatchException(agentId, toolName, expectedFingerprint);

            logger.LogWarning(
                "[ToolFingerprint] Agent '{AgentId}': tool '{Tool}' fingerprint '{Fp}…' ausente — caindo para latest (flag allowMismatch=true).",
                agentId, toolName, expectedFingerprint[..Math.Min(12, expectedFingerprint.Length)]);
        }

        if (projectId is not null)
            return registry.TryGet(toolName, projectId, out var projFn) ? projFn : null;

        return registry.TryGet(toolName, out var fn) ? fn : null;
    }

    private static ChatResponseFormat? BuildResponseFormat(
        AgentStructuredOutputDefinition? structuredOutput,
        ILogger logger,
        string agentId)
    {
        if (structuredOutput is null)
            return null;

        return structuredOutput.ResponseFormat.ToLowerInvariant() switch
        {
            "json" => ChatResponseFormat.Json,
            "json_schema" when structuredOutput.Schema is not null =>
                ChatResponseFormat.ForJsonSchema(
                    structuredOutput.Schema.RootElement.Clone(),
                    structuredOutput.SchemaName ?? "response",
                    structuredOutput.SchemaDescription),
            "json_schema" =>
                LogAndReturnNull(logger, agentId, "json_schema format requires a Schema definition"),
            "text" => null,
            _ => LogAndReturnNull(logger, agentId, $"Unknown responseFormat '{structuredOutput.ResponseFormat}'")
        };
    }

    private static ChatResponseFormat? LogAndReturnNull(ILogger logger, string agentId, string message)
    {
        logger.LogWarning("Agent '{AgentId}': {Message} — using default text format.", agentId, message);
        return null;
    }
}
