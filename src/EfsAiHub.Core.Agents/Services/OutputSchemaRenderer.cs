using System.Text.Json;
using System.Text.Json.Nodes;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.RouterIntents;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Renderiza o JSON Schema final do <see cref="AgentStructuredOutputDefinition"/>:
///   * Routers ganham <c>properties.intent.enum</c> populado com os nomes
///     das intents resolvidas.
///   * Agentes com OperationalMemory ganham wrapper <c>{response,
///     operationalMemory}</c> ou injeção do field <c>operationalMemory</c>
///     direto no schema declarado.
/// Saída é o que será persistido em <see cref="AgentStructuredOutputDefinition.Schema"/>
/// — runtime consome direto sem mutar.
/// </summary>
public static class OutputSchemaRenderer
{
    private const string MemoryFieldName = "operationalMemory";

    public static AgentStructuredOutputDefinition? Render(
        AgentDefinition definition,
        IReadOnlyList<RouterIntent>? routerIntents)
    {
        var structuredOutput = definition.StructuredOutput;
        var hasMemory = definition.OperationalMemory?.Schema is not null;

        if (structuredOutput is null && !hasMemory)
            return null;

        // Sem memória: aplica apenas a injeção de enum quando aplicável.
        if (!hasMemory)
        {
            if (structuredOutput is null)
                return null;

            return new AgentStructuredOutputDefinition
            {
                ResponseFormat = structuredOutput.ResponseFormat,
                SchemaName = structuredOutput.SchemaName,
                SchemaDescription = structuredOutput.SchemaDescription,
                Schema = ApplyRouterEnumIfApplicable(definition, structuredOutput, routerIntents),
            };
        }

        // Com memória: enriquece o schema base com a property `operationalMemory`.
        return BuildResponseWithMemory(definition, structuredOutput, routerIntents);
    }

    private static JsonDocument? ApplyRouterEnumIfApplicable(
        AgentDefinition definition,
        AgentStructuredOutputDefinition structuredOutput,
        IReadOnlyList<RouterIntent>? routerIntents)
    {
        if (structuredOutput.Schema is null) return null;
        if (definition.Type != AgentType.Router
            || routerIntents is not { Count: > 0 })
        {
            return structuredOutput.Schema;
        }

        var raw = structuredOutput.Schema.RootElement.GetRawText();
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            // Schema malformado: mantém o que está persistido. Backend já
            // valida count >= 2 intents no save; mismatch aqui é alerta de
            // dado corrompido, mas não justifica derrubar o publish.
            return structuredOutput.Schema;
        }

        if (parsed is not JsonObject root
            || root["properties"] is not JsonObject properties
            || properties["intent"] is not JsonObject intentNode)
        {
            return structuredOutput.Schema;
        }

        var enumArr = new JsonArray();
        foreach (var intent in routerIntents)
            enumArr.Add(intent.Name);
        intentNode["enum"] = enumArr;

        return JsonDocument.Parse(root.ToJsonString());
    }

    private static AgentStructuredOutputDefinition BuildResponseWithMemory(
        AgentDefinition definition,
        AgentStructuredOutputDefinition? structuredOutput,
        IReadOnlyList<RouterIntent>? routerIntents)
    {
        var memSchemaJson = definition.OperationalMemory!.Schema!.RootElement.GetRawText();
        var memNode = JsonNode.Parse(memSchemaJson)
            ?? throw new DomainException(
                $"Agent '{definition.Id}': OperationalMemory.Schema inválido (parse falhou).");

        JsonObject root;
        string schemaName;
        string? schemaDescription;
        string responseFormat;

        var hasJsonSchema = structuredOutput is not null
            && string.Equals(structuredOutput.ResponseFormat, "json_schema", StringComparison.OrdinalIgnoreCase)
            && structuredOutput.Schema is not null;

        if (hasJsonSchema)
        {
            var baseSchema = ApplyRouterEnumIfApplicable(definition, structuredOutput!, routerIntents);
            var baseJson = baseSchema!.RootElement.GetRawText();
            var baseRoot = JsonNode.Parse(baseJson)
                ?? throw new DomainException(
                    $"Agent '{definition.Id}': StructuredOutput.Schema inválido (parse falhou).");
            if (baseRoot is not JsonObject baseObj)
                throw new DomainException(
                    $"Agent '{definition.Id}': StructuredOutput.Schema deve ser um objeto JSON na raiz.");

            root = baseObj;
            schemaName = structuredOutput!.SchemaName ?? "response";
            schemaDescription = structuredOutput.SchemaDescription;
            responseFormat = structuredOutput.ResponseFormat;
        }
        else
        {
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
            responseFormat = "json_schema";
        }

        var properties = root["properties"] as JsonObject;
        if (properties is null)
        {
            properties = new JsonObject();
            root["properties"] = properties;
        }

        // Schemas canônicos (ex: Router formal) já declaram operationalMemory —
        // nesse caso não sobrescreve. Caso contrário, injeta a property pra que
        // o LLM saiba emitir o campo e o middleware persista.
        if (!properties.ContainsKey(MemoryFieldName))
            properties[MemoryFieldName] = memNode;

        var required = root["required"] as JsonArray ?? new JsonArray();
        if (!required.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == MemoryFieldName))
            required.Add(MemoryFieldName);
        root["required"] = required;

        var composed = JsonDocument.Parse(root.ToJsonString());

        return new AgentStructuredOutputDefinition
        {
            ResponseFormat = responseFormat,
            SchemaName = schemaName,
            SchemaDescription = schemaDescription,
            Schema = composed,
        };
    }
}
