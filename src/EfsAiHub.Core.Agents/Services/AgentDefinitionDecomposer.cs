using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Boundary de leitura: recebe a <see cref="AgentDefinition"/> final
/// persistida no banco (texto + tools/model expandidos) e devolve a forma
/// editável que o cliente conhece (apenas autoral + IDs de dependências).
/// O texto autoral vive em <see cref="AgentDefinition.AuthorInstructions"/>
/// — o decomposer apenas o copia pra <see cref="AgentDefinition.Instructions"/>
/// pra preservar o shape esperado pelos clientes do GET.
/// </summary>
public interface IAgentDefinitionDecomposer
{
    AgentDefinition Decompose(AgentDefinition stored);
}

public sealed class AgentDefinitionDecomposer : IAgentDefinitionDecomposer
{
    private static readonly Regex LegacyMarkerPattern =
        new(@"<!--\s*aihub:auto-(?:intents|skills|worker-scope)(?:-end)?\s*-->",
            RegexOptions.Compiled);

    private readonly ILogger<AgentDefinitionDecomposer> _logger;

    public AgentDefinitionDecomposer(ILogger<AgentDefinitionDecomposer>? logger = null)
    {
        _logger = logger ?? NullLogger<AgentDefinitionDecomposer>.Instance;
    }

    public AgentDefinition Decompose(AgentDefinition stored)
    {
        var authorInstructions = ResolveAuthorInstructions(stored);
        var authorTools = StripExpandedTools(stored.Tools);
        var authorModel = StripExpandedModel(stored.Model);
        var authorStructuredOutput = StripStructuredOutputForAuthor(stored);
        var authorMiddlewares = StripAutoInjectedMiddlewares(stored);

        return new AgentDefinition
        {
            Id = stored.Id,
            Name = stored.Name,
            Description = stored.Description,
            Type = stored.Type,
            RouterIntentIds = stored.RouterIntentIds,
            Model = authorModel,
            Provider = stored.Provider,
            AuthorInstructions = authorInstructions,
            // Editor consome o texto cru via AuthorInstructions; o campo
            // Instructions é o composto que vai pro LLM e não tem valor
            // pro cliente de edição — devolvemos null pra evitar que o
            // front renderize o composto por engano.
            Instructions = null,
            Tools = authorTools,
            StructuredOutput = authorStructuredOutput,
            OperationalMemory = stored.OperationalMemory,
            Middlewares = authorMiddlewares,
            FallbackProvider = stored.FallbackProvider,
            Resilience = stored.Resilience,
            CostBudget = stored.CostBudget,
            SkillRefs = stored.SkillRefs,
            Metadata = stored.Metadata,
            ProjectId = stored.ProjectId,
            TenantId = stored.TenantId,
            Visibility = stored.Visibility,
            AllowedProjectIds = stored.AllowedProjectIds,
            Enabled = stored.Enabled,
            CreatedAt = stored.CreatedAt,
            UpdatedAt = stored.UpdatedAt,
            RegressionTestSetId = stored.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = stored.RegressionEvaluatorConfigVersionId,
            LastChatSandboxValidatedAt = stored.LastChatSandboxValidatedAt,
            LastChatSandboxValidatedByUserId = stored.LastChatSandboxValidatedByUserId,
            LastChatSandboxValidatedAgentVersionId = stored.LastChatSandboxValidatedAgentVersionId,
        };
    }

    private string? ResolveAuthorInstructions(AgentDefinition stored)
    {
        // Caminho canônico: campo populado pelo composer no save. Editor
        // sempre lê daqui — zero parsing, zero ambiguidade.
        if (!string.IsNullOrEmpty(stored.AuthorInstructions))
            return stored.AuthorInstructions;

        // Sem AuthorInstructions e sem Instructions = agente sem texto.
        if (string.IsNullOrEmpty(stored.Instructions))
            return null;

        // Fallback pra row pré-migration: Instructions ainda tem marcadores
        // legados embutidos. Limpa e log warning — backfill deve ter rodado
        // antes de servir tráfego; chegar aqui pós-deploy é bug.
        if (LegacyMarkerPattern.IsMatch(stored.Instructions))
        {
            _logger.LogWarning(
                "Agent '{AgentId}' ainda tem marcadores legados em Instructions e AuthorInstructions vazio. " +
                "Rodar backfill 012 antes da próxima edição.",
                stored.Id);
            return StripLegacyAutoBlocks(stored.Instructions);
        }

        // Sem marcadores, sem AuthorInstructions — Instructions parece ser
        // texto autoral cru sem composição. Tratamos como autoral.
        return stored.Instructions;
    }

    private static string? StripLegacyAutoBlocks(string instructions)
    {
        // Padrão único cobre os 3 blocos pareados (intents/skills/worker-scope).
        // Conteúdo entre Begin e End é descartado junto.
        var pattern = @"<!--\s*aihub:auto-(?:intents|skills|worker-scope)\s*-->" +
            @"[\s\S]*?" +
            @"<!--\s*aihub:auto-(?:intents|skills|worker-scope)-end\s*-->\s*";
        var stripped = Regex.Replace(instructions, pattern, string.Empty).TrimEnd();
        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }

    private static IReadOnlyList<AgentToolDefinition> StripExpandedTools(IReadOnlyList<AgentToolDefinition> tools)
    {
        if (tools.Count == 0) return tools;

        var result = new List<AgentToolDefinition>(tools.Count);
        foreach (var tool in tools)
        {
            // Tools provenientes de skill foram mescladas pelo composer; cliente
            // não enviou e nem espera ver no GET.
            if (!string.IsNullOrEmpty(tool.SourceSkillId)) continue;

            // generic_http com expansão volta pra forma compacta (apenas
            // referência por id) — o cliente edita a tool no recurso dedicado,
            // não inline no agente.
            if (string.Equals(tool.Type, "generic_http", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(tool.GenericToolId))
            {
                result.Add(new AgentToolDefinition
                {
                    Type = tool.Type,
                    Name = tool.Name,
                    GenericToolId = tool.GenericToolId,
                    RequiresApproval = tool.RequiresApproval,
                    FingerprintHash = tool.FingerprintHash,
                });
                continue;
            }

            // Demais tipos (function, mcp, code_interpreter, web_search, file_search)
            // não têm expansão automática hoje — o cliente declara tudo inline.
            result.Add(tool);
        }

        return result;
    }

    private static AgentModelConfig StripExpandedModel(AgentModelConfig model)
    {
        // Sem preset, cliente declarou tudo manualmente — preserva.
        if (string.IsNullOrEmpty(model.PredefinedModelId)) return model;

        // Com preset, DeploymentName/Temperature/MaxTokens vieram do catálogo
        // expandido. Cliente edita o preset no recurso dedicado.
        return new AgentModelConfig
        {
            DeploymentName = string.Empty,
            PredefinedModelId = model.PredefinedModelId,
        };
    }

    /// <summary>
    /// Reverte as transformações que o composer/template aplica no
    /// <c>StructuredOutput</c> pra devolver pro editor a forma autoral:
    /// <list type="bullet">
    ///   <item><b>Router</b>: o composer injeta enum dinâmico em
    ///   <c>properties.intent</c> com os names das intents. Editor declara
    ///   o intent como <c>{ type: "string" }</c> puro; removemos o enum.</item>
    ///   <item><b>Conversational</b>: o template envolve o subschema autoral
    ///   num wrap canônico <c>{ output_type, output_status, message, output }</c>.
    ///   Editor declara apenas o subschema interno; devolvemos <c>properties.output</c>
    ///   como o schema puro.</item>
    ///   <item><b>OperationalMemory</b> (todos os tipos): quando o agent tem
    ///   memória habilitada, o <c>OutputSchemaRenderer</c> injeta a property
    ///   <c>operationalMemory</c> no schema. Editor não declarou essa property —
    ///   ela vive no campo top-level <c>OperationalMemory.Schema</c>. Removemos
    ///   pra evitar shape duplicado no form.</item>
    /// </list>
    /// </summary>
    private static AgentStructuredOutputDefinition? StripStructuredOutputForAuthor(AgentDefinition stored)
    {
        var structuredOutput = stored.StructuredOutput;
        if (structuredOutput?.Schema is null) return structuredOutput;

        var typeStripped = stored.Type switch
        {
            AgentType.Router => StripRouterEnum(structuredOutput),
            AgentType.Conversational => UnwrapConversationalSchema(structuredOutput),
            _ => structuredOutput,
        };

        // Pós-passo: tira `operationalMemory` do schema quando o agent tem
        // memória habilitada. O wrap conversational já descarta isso ao
        // pegar `properties.output`; pros demais tipos é necessário aqui.
        if (typeStripped?.Schema is not null
            && stored.OperationalMemory?.Schema is not null)
        {
            return StripOperationalMemoryProperty(typeStripped);
        }

        return typeStripped;
    }

    private static AgentStructuredOutputDefinition? StripRouterEnum(AgentStructuredOutputDefinition structuredOutput)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(structuredOutput.Schema!.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return structuredOutput;
        }

        if (parsed is not JsonObject root
            || root["properties"] is not JsonObject properties
            || properties["intent"] is not JsonObject intentNode
            || intentNode["enum"] is null)
        {
            return structuredOutput;
        }

        intentNode.Remove("enum");
        var doc = JsonDocument.Parse(root.ToJsonString());
        return new AgentStructuredOutputDefinition
        {
            ResponseFormat = structuredOutput.ResponseFormat,
            SchemaName = structuredOutput.SchemaName,
            SchemaDescription = structuredOutput.SchemaDescription,
            Schema = doc,
        };
    }

    /// <summary>
    /// Remove middlewares que o <see cref="IAgentTemplateService"/> injeta
    /// automaticamente em runtime mas que o user nunca declarou no editor.
    /// O middleware continua presente no snapshot persistido (runtime precisa
    /// dele); só sumimos da view de edição pra que re-saves não exponham
    /// detalhes do template.
    /// </summary>
    /// <remarks>
    /// Hoje cobre:
    /// <list type="bullet">
    ///   <item><b>StructuredOutputState</b> em <see cref="AgentType.Conversational"/>:
    ///   o <see cref="IAgentTemplateService"/> sempre injeta esse middleware no
    ///   apply pra emitir STATE_DELTA via SSE — o user não controla esse aspecto
    ///   diretamente.</item>
    /// </list>
    /// Settings declaradas pelo user (ex.: <c>stateKey</c>) seriam perdidas no
    /// strip — hoje o template service não persiste settings autorais nesse
    /// middleware, então não há regressão. Se isso mudar, lembrar de preservar.
    /// </remarks>
    private static IReadOnlyList<AgentMiddlewareConfig> StripAutoInjectedMiddlewares(AgentDefinition stored)
    {
        if (stored.Middlewares.Count == 0) return stored.Middlewares;
        if (stored.Type != AgentType.Conversational) return stored.Middlewares;

        const string AutoInjectedType = "StructuredOutputState";

        var hasAutoInjected = stored.Middlewares.Any(m =>
            string.Equals(m.Type, AutoInjectedType, StringComparison.OrdinalIgnoreCase));
        if (!hasAutoInjected) return stored.Middlewares;

        return stored.Middlewares
            .Where(m => !string.Equals(m.Type, AutoInjectedType, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>
    /// Remove a property <c>operationalMemory</c> e a entry correspondente em
    /// <c>required</c> do schema retornado, quando ela foi injetada pelo
    /// <c>OutputSchemaRenderer</c> a partir de <c>OperationalMemory.Schema</c>.
    /// A memória continua disponível no campo top-level <c>OperationalMemory</c>
    /// do response — o editor renderiza a partir de lá, sem ver a duplicação
    /// no schema do output.
    /// </summary>
    private static AgentStructuredOutputDefinition StripOperationalMemoryProperty(AgentStructuredOutputDefinition structuredOutput)
    {
        const string MemoryFieldName = "operationalMemory";

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(structuredOutput.Schema!.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return structuredOutput;
        }

        if (parsed is not JsonObject root) return structuredOutput;

        var properties = root["properties"] as JsonObject;
        var required = root["required"] as JsonArray;

        var hadInProperties = properties is not null && properties.ContainsKey(MemoryFieldName);
        var hadInRequired = required is not null
            && required.Any(n => n is JsonValue v && v.TryGetValue<string>(out var s) && s == MemoryFieldName);

        if (!hadInProperties && !hadInRequired) return structuredOutput;

        properties?.Remove(MemoryFieldName);

        if (required is not null)
        {
            for (var i = required.Count - 1; i >= 0; i--)
            {
                if (required[i] is JsonValue v
                    && v.TryGetValue<string>(out var s)
                    && s == MemoryFieldName)
                {
                    required.RemoveAt(i);
                }
            }
        }

        var doc = JsonDocument.Parse(root.ToJsonString());
        return new AgentStructuredOutputDefinition
        {
            ResponseFormat = structuredOutput.ResponseFormat,
            SchemaName = structuredOutput.SchemaName,
            SchemaDescription = structuredOutput.SchemaDescription,
            Schema = doc,
        };
    }

    /// <summary>
    /// Detecta o wrap canônico do Conversational
    /// (<c>output_type/output_status/message/output</c>) e devolve apenas o
    /// subschema interno (<c>properties.output</c>) — que é o que o editor
    /// exibe pro user. Quando o wrap não é reconhecido (schema custom ou sem
    /// <c>properties.output</c>), preserva o schema cru pra não destruir
    /// input legítimo.
    /// </summary>
    private static AgentStructuredOutputDefinition? UnwrapConversationalSchema(AgentStructuredOutputDefinition structuredOutput)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(structuredOutput.Schema!.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return structuredOutput;
        }

        if (parsed is not JsonObject root
            || root["properties"] is not JsonObject properties)
        {
            return structuredOutput;
        }

        var hasCanonicalShape = properties.ContainsKey("output_type")
            && properties.ContainsKey("output_status")
            && properties.ContainsKey("message");

        if (!hasCanonicalShape) return structuredOutput;
        if (properties["output"] is not JsonObject outputSubSchema) return null;

        // Subschema autoral preservado intacto; SchemaName/SchemaDescription do
        // wrap canônico são descartados — o editor não usa esses campos (o
        // user só edita o shape interno).
        var doc = JsonDocument.Parse(outputSubSchema.ToJsonString());
        return new AgentStructuredOutputDefinition
        {
            ResponseFormat = structuredOutput.ResponseFormat,
            SchemaName = null,
            SchemaDescription = null,
            Schema = doc,
        };
    }
}
