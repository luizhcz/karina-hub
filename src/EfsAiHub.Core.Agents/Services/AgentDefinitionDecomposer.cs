using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Boundary de leitura: recebe a <see cref="AgentDefinition"/> final
/// persistida no banco (texto + tools/model expandidos) e devolve a forma
/// editável que o cliente conhece (apenas autoral + IDs de dependências).
/// Inverso de <see cref="AgentDefinitionComposer"/>: <c>Decompose(Compose(x)) ≡ x</c>
/// por construção dos marcadores estáveis.
/// </summary>
public interface IAgentDefinitionDecomposer
{
    AgentDefinition Decompose(AgentDefinition stored);
}

public sealed class AgentDefinitionDecomposer : IAgentDefinitionDecomposer
{
    public AgentDefinition Decompose(AgentDefinition stored)
    {
        var authorInstructions = StripAutoBlocks(stored.Instructions);
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
            Instructions = authorInstructions,
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

    private static string? StripAutoBlocks(string? instructions)
    {
        if (string.IsNullOrEmpty(instructions)) return instructions;

        var stripped = instructions;
        stripped = StripBlock(stripped, AgentInstructionsMarkers.IntentsBegin, AgentInstructionsMarkers.IntentsEnd);
        stripped = StripBlock(stripped, AgentInstructionsMarkers.SkillsBegin, AgentInstructionsMarkers.SkillsEnd);
        stripped = StripBlock(stripped, AgentInstructionsMarkers.WorkerScopeBegin, AgentInstructionsMarkers.WorkerScopeEnd);

        // O composer separa cada bloco com "\n\n"; após o strip, espaços
        // residuais ficam no fim do autoral. TrimEnd remove sem mexer no
        // conteúdo do owner — entradas vazias entre parágrafos do autoral
        // permanecem intactas.
        stripped = stripped.TrimEnd();

        return string.IsNullOrEmpty(stripped) ? null : stripped;
    }

    private static string StripBlock(string source, string begin, string end)
    {
        // Markers são HTML comments com caracteres especiais regex (-, !, --).
        // Escape garante match literal independente do conteúdo no entorno.
        var pattern = $@"{Regex.Escape(begin)}[\s\S]*?{Regex.Escape(end)}\s*";
        return Regex.Replace(source, pattern, string.Empty);
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
