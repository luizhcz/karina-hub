using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.GenericTools;
using EfsAiHub.Core.Agents.RouterIntents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents.Services;

/// <summary>
/// Boundary de escrita: recebe a <see cref="AgentDefinition"/> autoral (texto
/// + IDs de dependências) que veio do CRUD e devolve a definição final
/// autocontida que vai pro banco e pro snapshot. Runtime nunca volta às
/// dependências em runtime — leitura única do snapshot resolve tudo.
///
/// Falhas de resolução (intent/tool/skill/model removidos) lançam
/// <see cref="DomainException"/> — publish não pode produzir snapshot com
/// referência quebrada.
/// </summary>
public interface IAgentDefinitionComposer
{
    Task<AgentDefinition> ComposeAsync(AgentDefinition input, CancellationToken ct = default);
}

public sealed class AgentDefinitionComposer : IAgentDefinitionComposer
{
    private readonly IGenericToolRepository _genericTools;
    private readonly IPredefinedModelRepository _predefinedModels;
    private readonly IAgentRouterIntentLinkRepository? _routerIntentLinks;
    private readonly ISkillResolver _skillResolver;

    public AgentDefinitionComposer(
        IGenericToolRepository genericTools,
        IPredefinedModelRepository predefinedModels,
        ISkillResolver skillResolver,
        IAgentRouterIntentLinkRepository? routerIntentLinks = null)
    {
        _genericTools = genericTools;
        _predefinedModels = predefinedModels;
        _skillResolver = skillResolver;
        _routerIntentLinks = routerIntentLinks;
    }

    public async Task<AgentDefinition> ComposeAsync(AgentDefinition input, CancellationToken ct = default)
    {
        var resolvedModel = await ResolvePredefinedModelAsync(input, ct);
        var resolvedTools = await ResolveGenericToolsAsync(input, ct);
        var skills = await ResolveSkillsAsync(input, ct);
        var routerIntents = await ResolveRouterIntentsAsync(input, ct);

        var skillTools = MergeSkillTools(resolvedTools, skills);
        var instructions = PromptRenderer.Render(
            input.Type,
            input.Instructions,
            input.Metadata,
            routerIntents,
            skills);

        var structuredOutput = OutputSchemaRenderer.Render(
            new AgentDefinition
            {
                Id = input.Id,
                Name = input.Name,
                Model = input.Model,
                Type = input.Type,
                StructuredOutput = input.StructuredOutput,
                OperationalMemory = input.OperationalMemory,
            },
            routerIntents);

        var routerIntentIds = routerIntents is { Count: > 0 }
            ? routerIntents.Select(i => i.Id).ToList()
            : input.RouterIntentIds;

        return new AgentDefinition
        {
            Id = input.Id,
            Name = input.Name,
            Description = input.Description,
            Type = input.Type,
            RouterIntentIds = routerIntentIds,
            Model = resolvedModel,
            Provider = input.Provider,
            Instructions = instructions,
            Tools = skillTools,
            StructuredOutput = structuredOutput,
            OperationalMemory = input.OperationalMemory,
            Middlewares = input.Middlewares,
            FallbackProvider = input.FallbackProvider,
            Resilience = input.Resilience,
            CostBudget = input.CostBudget,
            SkillRefs = input.SkillRefs,
            Metadata = input.Metadata,
            ProjectId = input.ProjectId,
            TenantId = input.TenantId,
            Visibility = input.Visibility,
            AllowedProjectIds = input.AllowedProjectIds,
            Enabled = input.Enabled,
            CreatedAt = input.CreatedAt,
            UpdatedAt = input.UpdatedAt,
            RegressionTestSetId = input.RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = input.RegressionEvaluatorConfigVersionId,
            LastChatSandboxValidatedAt = input.LastChatSandboxValidatedAt,
            LastChatSandboxValidatedByUserId = input.LastChatSandboxValidatedByUserId,
            LastChatSandboxValidatedAgentVersionId = input.LastChatSandboxValidatedAgentVersionId,
        };
    }

    private async Task<AgentModelConfig> ResolvePredefinedModelAsync(AgentDefinition input, CancellationToken ct)
    {
        var presetId = input.Model.PredefinedModelId;
        if (string.IsNullOrWhiteSpace(presetId))
            return input.Model;

        var preset = await _predefinedModels.GetByIdAsync(presetId!, ct)
            ?? throw new DomainException(
                $"Agent '{input.Id}': PredefinedModel '{presetId}' referenciado não foi encontrado.");

        if (!preset.Enabled)
            throw new DomainException(
                $"Agent '{input.Id}': PredefinedModel '{presetId}' está desabilitado — publish bloqueado.");

        // Os campos resolvidos viram a fonte de verdade no snapshot. O cliente
        // pode até ter enviado DeploymentName=null (default quando preset está
        // setado) — o composer sobrescreve com o valor expandido.
        return new AgentModelConfig
        {
            DeploymentName = preset.DeploymentName,
            Temperature = input.Model.Temperature ?? preset.DefaultTemperature,
            MaxTokens = input.Model.MaxTokens ?? preset.DefaultMaxTokens,
            PredefinedModelId = preset.Id,
        };
    }

    private async Task<IReadOnlyList<AgentToolDefinition>> ResolveGenericToolsAsync(
        AgentDefinition input,
        CancellationToken ct)
    {
        if (input.Tools.Count == 0) return input.Tools;

        var resolved = new List<AgentToolDefinition>(input.Tools.Count);
        foreach (var tool in input.Tools)
        {
            if (!string.Equals(tool.Type, "generic_http", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(tool.GenericToolId))
            {
                resolved.Add(tool);
                continue;
            }

            var generic = await _genericTools.GetByIdAsync(tool.GenericToolId!, input.ProjectId, ct)
                ?? throw new DomainException(
                    $"Agent '{input.Id}': GenericTool '{tool.GenericToolId}' não foi encontrado no projeto '{input.ProjectId}'.");

            resolved.Add(new AgentToolDefinition
            {
                Type = tool.Type,
                Name = string.IsNullOrEmpty(tool.Name) ? generic.Name : tool.Name,
                RequiresApproval = tool.RequiresApproval,
                FingerprintHash = tool.FingerprintHash,
                McpServerId = tool.McpServerId,
                ServerLabel = tool.ServerLabel,
                ServerUrl = tool.ServerUrl,
                AllowedTools = new List<string>(tool.AllowedTools),
                RequireApproval = tool.RequireApproval,
                Headers = new Dictionary<string, string>(tool.Headers),
                ConnectionId = tool.ConnectionId,
                GenericToolId = generic.Id,
                HttpMethod = generic.HttpMethod,
                UrlTemplate = generic.UrlTemplate,
                PathParams = new Dictionary<string, ParamDefinition>(generic.PathParams),
                QueryParams = new Dictionary<string, ParamDefinition>(generic.QueryParams),
                CustomHeaders = new Dictionary<string, string>(generic.CustomHeaders),
                InputContentType = generic.InputContentType,
                InputSchemaJson = generic.InputSchema,
                OutputContentType = generic.OutputContentType,
                OutputSchemaJson = generic.OutputSchema,
                OutputProjectionMode = generic.OutputProjectionMode,
                TimeoutSecondsOverride = generic.TimeoutSecondsOverride,
                IsExclusive = generic.IsExclusive,
                SourceSkillId = tool.SourceSkillId,
            });
        }

        return resolved;
    }

    private async Task<IReadOnlyList<Skill>> ResolveSkillsAsync(AgentDefinition input, CancellationToken ct)
    {
        if (input.SkillRefs.Count == 0) return Array.Empty<Skill>();

        var resolved = new List<Skill>(input.SkillRefs.Count);
        foreach (var skillRef in input.SkillRefs)
        {
            var skill = await _skillResolver.ResolveAsync(skillRef, ownerProjectId: null, ct)
                ?? throw new DomainException(
                    $"Agent '{input.Id}': Skill '{skillRef.SkillId}' (version={skillRef.SkillVersionId}) " +
                    "referenciada não foi encontrada — publish bloqueado.");
            resolved.Add(skill);
        }

        return resolved;
    }

    private async Task<IReadOnlyList<RouterIntent>?> ResolveRouterIntentsAsync(
        AgentDefinition input,
        CancellationToken ct)
    {
        if (input.Type != AgentType.Router) return null;
        if (_routerIntentLinks is null) return null;

        return await _routerIntentLinks.ListIntentsForAgentAsync(input.Id, ct);
    }

    private static IReadOnlyList<AgentToolDefinition> MergeSkillTools(
        IReadOnlyList<AgentToolDefinition> authorTools,
        IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0) return authorTools;

        var seen = new HashSet<string>(
            authorTools
                .Where(t => !string.IsNullOrEmpty(t.Name))
                .Select(t => $"{t.Type}:{t.Name}"),
            StringComparer.OrdinalIgnoreCase);

        var merged = new List<AgentToolDefinition>(authorTools);
        foreach (var skill in skills)
        {
            foreach (var tool in skill.Tools)
            {
                var key = $"{tool.Type}:{tool.Name}";
                if (!string.IsNullOrEmpty(tool.Name) && !seen.Add(key))
                    continue;

                merged.Add(new AgentToolDefinition
                {
                    Type = tool.Type,
                    Name = tool.Name,
                    RequiresApproval = tool.RequiresApproval,
                    FingerprintHash = tool.FingerprintHash,
                    McpServerId = tool.McpServerId,
                    ServerLabel = tool.ServerLabel,
                    ServerUrl = tool.ServerUrl,
                    AllowedTools = new List<string>(tool.AllowedTools),
                    RequireApproval = tool.RequireApproval,
                    Headers = new Dictionary<string, string>(tool.Headers),
                    ConnectionId = tool.ConnectionId,
                    GenericToolId = tool.GenericToolId,
                    HttpMethod = tool.HttpMethod,
                    UrlTemplate = tool.UrlTemplate,
                    PathParams = tool.PathParams,
                    QueryParams = tool.QueryParams,
                    CustomHeaders = tool.CustomHeaders,
                    InputContentType = tool.InputContentType,
                    InputSchemaJson = tool.InputSchemaJson,
                    OutputContentType = tool.OutputContentType,
                    OutputSchemaJson = tool.OutputSchemaJson,
                    OutputProjectionMode = tool.OutputProjectionMode,
                    TimeoutSecondsOverride = tool.TimeoutSecondsOverride,
                    IsExclusive = tool.IsExclusive,
                    SourceSkillId = skill.Id,
                });
            }
        }

        return merged;
    }
}
