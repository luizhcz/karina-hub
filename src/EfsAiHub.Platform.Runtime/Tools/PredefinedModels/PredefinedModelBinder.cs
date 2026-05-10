using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Platform.Runtime.Tools.PredefinedModels;

public sealed class PredefinedModelBinder : IPredefinedModelBinder
{
    private readonly IPredefinedModelRepository _repo;
    private readonly ILogger<PredefinedModelBinder> _logger;

    public PredefinedModelBinder(
        IPredefinedModelRepository repo,
        ILogger<PredefinedModelBinder> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<AgentDefinition> BindAsync(AgentDefinition definition, CancellationToken ct = default)
    {
        var presetId = definition.Model?.PredefinedModelId;
        if (string.IsNullOrWhiteSpace(presetId)) return definition;

        var preset = await _repo.GetByIdAsync(presetId, ct);
        if (preset is null)
        {
            _logger.LogWarning(
                "[PredefinedModelBinder] Agent '{AgentId}' referencia PredefinedModelId='{PresetId}' que não existe — agente seguirá sem hidratação.",
                definition.Id, presetId);
            return definition;
        }

        if (!preset.Enabled)
        {
            _logger.LogWarning(
                "[PredefinedModelBinder] Agent '{AgentId}' referencia preset '{PresetId}' atualmente DESABILITADO — agente seguirá sem hidratação.",
                definition.Id, presetId);
            return definition;
        }

        var hydratedModel = new AgentModelConfig
        {
            DeploymentName = preset.DeploymentName,
            Temperature = preset.DefaultTemperature ?? definition.Model!.Temperature,
            MaxTokens = preset.DefaultMaxTokens ?? definition.Model!.MaxTokens,
            PredefinedModelId = preset.Id,
        };

        var hydratedProvider = new AgentProviderConfig
        {
            Type = preset.Provider,
            ClientType = preset.ClientType ?? definition.Provider.ClientType,
            Endpoint = preset.Endpoint ?? definition.Provider.Endpoint,
            // Preserva ApiKey: vem de InjectProjectCredentials ou config global,
            // não é parte do preset (separação secrets vs config).
            ApiKey = definition.Provider.ApiKey,
        };

        return CopyWithModelAndProvider(definition, hydratedModel, hydratedProvider);
    }

    private static AgentDefinition CopyWithModelAndProvider(
        AgentDefinition d,
        AgentModelConfig model,
        AgentProviderConfig provider) => new()
    {
        Id = d.Id,
        Name = d.Name,
        Description = d.Description,
        // Type/RouterIntentIds precisam ser preservados — Router que passa
        // por preset perdia o tipo e virava Custom silenciosamente.
        Type = d.Type,
        RouterIntentIds = d.RouterIntentIds,
        Model = model,
        Provider = provider,
        FallbackProvider = d.FallbackProvider,
        Instructions = d.Instructions,
        Tools = d.Tools,
        StructuredOutput = d.StructuredOutput,
        OperationalMemory = d.OperationalMemory,
        Middlewares = d.Middlewares,
        Resilience = d.Resilience,
        CostBudget = d.CostBudget,
        SkillRefs = d.SkillRefs,
        Metadata = d.Metadata,
        Visibility = d.Visibility,
        AllowedProjectIds = d.AllowedProjectIds,
        Enabled = d.Enabled,
        ProjectId = d.ProjectId,
        TenantId = d.TenantId,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        RegressionTestSetId = d.RegressionTestSetId,
        RegressionEvaluatorConfigVersionId = d.RegressionEvaluatorConfigVersionId,
    };
}
