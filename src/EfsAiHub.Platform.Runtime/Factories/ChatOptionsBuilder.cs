using System.Text.Json;
using System.Text.Json.Nodes;
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
        string? projectId = null,
        bool isStandaloneFlow = false,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents = null)
    {
        var chatOptions = BuildCoreOptions(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId, isStandaloneFlow, resolvedRouterIntents);

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
        string? projectId = null,
        bool isStandaloneFlow = false,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents = null)
    {
        return BuildCoreOptions(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId, isStandaloneFlow, resolvedRouterIntents);
    }

    private static ChatOptions BuildCoreOptions(
        AgentDefinition definition,
        IFunctionToolRegistry functionRegistry,
        ChannelWriter<ToolInvocation> toolWriter,
        ILogger<TrackedAIFunction> trackedFnLogger,
        ILogger logger,
        bool allowFingerprintMismatch,
        string? projectId,
        bool isStandaloneFlow = false,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents = null)
    {
        var options = new ChatOptions
        {
            Instructions = AppendRouterIntentBlockIfApplicable(
                definition,
                definition.Instructions,
                resolvedRouterIntents),
            Temperature = definition.Model.Temperature,
            MaxOutputTokens = definition.Model.MaxTokens,
            ModelId = definition.Model.DeploymentName
        };

        var tools = BuildFunctionTools(definition, functionRegistry, toolWriter, trackedFnLogger, logger, allowFingerprintMismatch, projectId);
        if (tools.Count > 0)
            options.Tools = tools;

        var responseFormat = BuildResponseFormat(definition, logger, isStandaloneFlow, resolvedRouterIntents);
        if (responseFormat is not null)
            options.ResponseFormat = responseFormat;

        // Log de inspeção: pra Router, dump das intents resolvidas + tamanho
        // do prompt final pós-injeção. Permite auditar o que o LLM recebe
        // sem precisar interceptar a chamada.
        if (definition.Type == AgentType.Router && resolvedRouterIntents is { Count: > 0 })
        {
            var enumNames = string.Join(", ", resolvedRouterIntents.Select(i => i.Name));
            logger.LogInformation(
                "[ChatOptionsBuilder] Router '{AgentId}' resolved {Count} intents: [{Names}]. " +
                "Final instructions length={Len} chars (block appended).",
                definition.Id, resolvedRouterIntents.Count, enumNames,
                options.Instructions?.Length ?? 0);
            logger.LogDebug(
                "[ChatOptionsBuilder] Router '{AgentId}' final instructions:\n{Instructions}",
                definition.Id, options.Instructions);
        }

        return options;
    }

    /// <summary>
    /// Pra Router com intents resolvidas, anexa bloco markdown
    /// <c># Intenções disponíveis</c> ao final do <c>Instructions</c> do
    /// agent. Bloco contém: nome técnico (em backticks) + descrição + exemplos
    /// inline. Sem marker no instructions persistido — runtime apenas
    /// concatena com separador, robusto a edits do user no skeleton.
    ///
    /// Ordem de impacto pro LLM: role/goal/output (skeleton) → bloco no fim
    /// (recência atende mais peso). Anti-leak explícito no header do bloco
    /// reforça "não inventar/combinar/fora do enum".
    /// </summary>
    private static string? AppendRouterIntentBlockIfApplicable(
        AgentDefinition definition,
        string? baseInstructions,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents)
    {
        if (definition.Type != AgentType.Router
            || resolvedRouterIntents is not { Count: > 0 })
        {
            return baseInstructions;
        }

        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(baseInstructions))
        {
            sb.Append(baseInstructions.TrimEnd());
            sb.Append("\n\n");
        }

        sb.Append("# Intenções disponíveis\n\n");
        sb.Append(
            "Escolha **exatamente uma** categoria do enum `intent` para cada input. " +
            "Não invente categorias. Não combine. Se nenhuma encaixar bem, escolha a mais próxima " +
            "e devolva `confidence < 0.5` para o workflow decidir.\n\n");

        foreach (var intent in resolvedRouterIntents)
        {
            var name = (intent.Name ?? string.Empty).Trim();
            if (name.Length == 0) continue;

            var description = (intent.Description ?? string.Empty).Trim();
            if (description.Length > 0)
                sb.Append($"- `{name}` — {description}\n");
            else
                sb.Append($"- `{name}`\n");

            var validExamples = intent.Examples?
                .Select(e => (e ?? string.Empty).Trim())
                .Where(e => e.Length > 0)
                .ToList() ?? new List<string>();

            if (validExamples.Count > 0)
            {
                sb.Append("  Exemplos:\n");
                foreach (var example in validExamples)
                    sb.Append($"  • \"{example}\"\n");
            }
        }

        return sb.ToString();
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

        // Resolve generic_http tools: o GenericToolBinder já registrou a
        // DynamicGenericAIFunction project-scoped no FunctionToolRegistry usando
        // GenericToolId como chave; aqui só recuperamos e envolvemos no tracker.
        foreach (var toolDef in definition.Tools.Where(t =>
            t.Type.Equals("generic_http", StringComparison.OrdinalIgnoreCase)))
        {
            var key = toolDef.GenericToolId;
            if (string.IsNullOrWhiteSpace(key))
            {
                logger.LogWarning(
                    "Agent '{AgentId}': generic_http tool sem GenericToolId — ignorada.", definition.Id);
                continue;
            }
            if (addedNames.Contains(key))
                continue;

            var found = projectId is not null
                ? functionRegistry.TryGet(key, projectId, out var fn)
                : functionRegistry.TryGet(key, out fn);
            if (!found || fn is null)
            {
                logger.LogWarning(
                    "Agent '{AgentId}': generic_http tool '{ToolId}' não encontrada no registry (binder pulou ou tool foi removido) — ignorada.",
                    definition.Id, key);
                continue;
            }

            tools.Add(new TrackedAIFunction(fn, definition.Id, toolWriter, trackedFnLogger));
            addedNames.Add(key);
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
        AgentDefinition definition,
        ILogger logger,
        bool isStandaloneFlow = false,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents = null)
    {
        var structuredOutput = definition.StructuredOutput;
        var memory = definition.OperationalMemory;

        // Workflow standalone não preserva contexto entre chamadas — não há
        // continuidade pra memória persistir. Pula a composição do schema com
        // operationalMemory (LLM não emite o campo) e cai no fluxo normal de
        // structured output. Pareado com bypass do middleware no AgentFactory
        // pra que pre-call não injete preamble nem state.
        if (memory?.Schema is not null && !isStandaloneFlow)
            return BuildResponseFormatWithMemory(definition, logger);

        if (structuredOutput is null)
            return null;

        return structuredOutput.ResponseFormat.ToLowerInvariant() switch
        {
            "json" => ChatResponseFormat.Json,
            "json_schema" when structuredOutput.Schema is not null =>
                BuildJsonSchemaFormat(definition, structuredOutput, resolvedRouterIntents, logger),
            "json_schema" =>
                LogAndReturnNull(logger, definition.Id, "json_schema format requires a Schema definition"),
            "text" => null,
            _ => LogAndReturnNull(logger, definition.Id, $"Unknown responseFormat '{structuredOutput.ResponseFormat}'")
        };
    }

    /// <summary>
    /// Materializa o ChatResponseFormat de json_schema. Pra Router com intents
    /// resolvidas, sobrescreve <c>properties.intent.enum</c> com os nomes do
    /// pool — assim edits no pool propagam pra próxima chamada do agent sem
    /// re-publish. Outros tipos usam o schema persistido como-é.
    /// </summary>
    private static ChatResponseFormat BuildJsonSchemaFormat(
        AgentDefinition definition,
        AgentStructuredOutputDefinition structuredOutput,
        IReadOnlyList<EfsAiHub.Core.Agents.RouterIntents.RouterIntent>? resolvedRouterIntents,
        ILogger logger)
    {
        JsonElement schemaElement;

        if (definition.Type == AgentType.Router
            && resolvedRouterIntents is { Count: > 0 })
        {
            var raw = structuredOutput.Schema!.RootElement.GetRawText();
            var node = JsonNode.Parse(raw) as JsonObject;
            if (node is not null
                && node["properties"] is JsonObject props
                && props["intent"] is JsonObject intentNode)
            {
                var enumArr = new JsonArray();
                foreach (var intent in resolvedRouterIntents)
                    enumArr.Add(intent.Name);
                intentNode["enum"] = enumArr;

                using var doc = JsonDocument.Parse(node.ToJsonString());
                schemaElement = doc.RootElement.Clone();
            }
            else
            {
                // Schema inesperado pra Router (sem properties.intent). Cai no
                // schema persistido — backend já valida hard count >= 2 no save.
                schemaElement = structuredOutput.Schema!.RootElement.Clone();
            }
        }
        else
        {
            schemaElement = structuredOutput.Schema!.RootElement.Clone();
        }

        return ChatResponseFormat.ForJsonSchema(
            schemaElement,
            structuredOutput.SchemaName ?? "response",
            structuredOutput.SchemaDescription);
    }

    /// <summary>
    /// Compõe ResponseFormat quando o agente tem memória operacional. Adiciona
    /// property <c>operationalMemory</c> ao schema do StructuredOutput existente
    /// (ou cria wrapper <c>{response, operationalMemory}</c> quando não há
    /// schema base). Throws quando o schema do agente já reserva o nome —
    /// melhor falhar cedo que sobrescrever silenciosamente.
    /// </summary>
    private static ChatResponseFormat? BuildResponseFormatWithMemory(
        AgentDefinition definition,
        ILogger logger)
    {
        const string MemoryFieldName = "operationalMemory";

        var memSchemaJson = definition.OperationalMemory!.Schema!.RootElement.GetRawText();
        var memNode = JsonNode.Parse(memSchemaJson)
            ?? throw new InvalidOperationException(
                $"Agent '{definition.Id}': OperationalMemory.Schema inválido (parse falhou).");

        JsonObject root;
        string schemaName;
        string? schemaDescription;

        var structuredOutput = definition.StructuredOutput;
        var hasJsonSchema = structuredOutput is not null
            && string.Equals(structuredOutput.ResponseFormat, "json_schema", StringComparison.OrdinalIgnoreCase)
            && structuredOutput.Schema is not null;

        if (hasJsonSchema)
        {
            var baseRoot = JsonNode.Parse(structuredOutput!.Schema!.RootElement.GetRawText())
                ?? throw new InvalidOperationException(
                    $"Agent '{definition.Id}': StructuredOutput.Schema inválido (parse falhou).");
            if (baseRoot is not JsonObject baseObj)
                throw new InvalidOperationException(
                    $"Agent '{definition.Id}': StructuredOutput.Schema deve ser um objeto JSON na raiz.");

            root = baseObj;
            schemaName = structuredOutput.SchemaName ?? "response";
            schemaDescription = structuredOutput.SchemaDescription;
        }
        else
        {
            // Sem schema base — wrapper mínimo onde a resposta livre vai em "response".
            root = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["response"] = new JsonObject { ["type"] = "string" },
                },
                ["required"] = new JsonArray { "response" },
            };
            schemaName = structuredOutput?.SchemaName ?? "response";
            schemaDescription = structuredOutput?.SchemaDescription;
        }

        var properties = root["properties"] as JsonObject;
        if (properties is null)
        {
            properties = new JsonObject();
            root["properties"] = properties;
        }

        if (properties.ContainsKey(MemoryFieldName))
            throw new InvalidOperationException(
                $"Agent '{definition.Id}': StructuredOutput.Schema já contém property '{MemoryFieldName}' — " +
                "remove do schema do agente ou desative OperationalMemory pra evitar colisão.");

        properties[MemoryFieldName] = memNode;

        var required = root["required"] as JsonArray ?? new JsonArray();
        if (!required.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == MemoryFieldName))
            required.Add(MemoryFieldName);
        root["required"] = required;

        using var composed = JsonDocument.Parse(root.ToJsonString());
        return ChatResponseFormat.ForJsonSchema(
            composed.RootElement.Clone(),
            schemaName,
            schemaDescription);
    }

    private static ChatResponseFormat? LogAndReturnNull(ILogger logger, string agentId, string message)
    {
        logger.LogWarning("Agent '{AgentId}': {Message} — using default text format.", agentId, message);
        return null;
    }
}
