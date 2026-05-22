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
        var authorStructuredOutput = StripRouterEnum(stored);

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
            Middlewares = stored.Middlewares,
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

    private static AgentStructuredOutputDefinition? StripRouterEnum(AgentDefinition stored)
    {
        var structuredOutput = stored.StructuredOutput;
        if (structuredOutput?.Schema is null) return structuredOutput;
        if (stored.Type != AgentType.Router) return structuredOutput;

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(structuredOutput.Schema.RootElement.GetRawText());
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
}
