using System.Text.Json;
using EfsAiHub.Core.Abstractions.Exceptions;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Payload mutável e parcial de um rascunho de agent. Difere de <see cref="AgentDefinition"/>
/// por aceitar estado incompleto: Name vazio, Model nulo, Tools ausentes — qualquer
/// campo opcional. Invariantes só rodam em <see cref="ToAgentDefinition"/> (chamado
/// no momento do publish), permitindo salvar/recarregar progresso parcial sem 400.
/// </summary>
public sealed class AgentDraftPayload
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public AgentModelConfig? Model { get; set; }
    public AgentProviderConfig? Provider { get; set; }
    public string? Instructions { get; set; }
    public IReadOnlyList<AgentToolDefinition>? Tools { get; set; }
    public AgentStructuredOutputDefinition? StructuredOutput { get; set; }
    public AgentOperationalMemoryDefinition? OperationalMemory { get; set; }
    public IReadOnlyList<AgentMiddlewareConfig>? Middlewares { get; set; }
    public AgentProviderConfig? FallbackProvider { get; set; }
    public ResiliencePolicy? Resilience { get; set; }
    public AgentCostBudget? CostBudget { get; set; }
    public IReadOnlyList<SkillRef>? SkillRefs { get; set; }
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// Visibility proposed pelo user no draft. Sem efeito até publish (drafts são
    /// sempre owner-only no nível físico — ver query filter em AgentDraftRow).
    /// </summary>
    public string? Visibility { get; set; }

    public IReadOnlyList<string>? AllowedProjectIds { get; set; }
    public bool? Enabled { get; set; }
    public string? RegressionTestSetId { get; set; }
    public string? RegressionEvaluatorConfigVersionId { get; set; }

    /// <summary>
    /// Materializa um <see cref="AgentDefinition"/> estrito — usado no publish.
    /// Aplica defaults pra campos opcionais ausentes e dispara
    /// <see cref="AgentDefinition.EnsureInvariants"/>; payload com Name/Model
    /// faltando lança <see cref="DomainException"/> aqui.
    /// </summary>
    public AgentDefinition ToAgentDefinition(
        string id,
        string projectId,
        string tenantId,
        DateTime? createdAt = null)
    {
        var def = new AgentDefinition
        {
            Id = id,
            Name = Name ?? string.Empty,
            Description = Description,
            Model = Model ?? new AgentModelConfig { DeploymentName = string.Empty },
            Provider = Provider ?? new AgentProviderConfig(),
            Instructions = Instructions,
            Tools = Tools ?? Array.Empty<AgentToolDefinition>(),
            StructuredOutput = StructuredOutput,
            OperationalMemory = OperationalMemory,
            Middlewares = Middlewares ?? Array.Empty<AgentMiddlewareConfig>(),
            FallbackProvider = FallbackProvider,
            Resilience = Resilience,
            CostBudget = CostBudget,
            SkillRefs = SkillRefs ?? Array.Empty<SkillRef>(),
            Metadata = Metadata ?? new Dictionary<string, string>(),
            Visibility = Visibility ?? "project",
            AllowedProjectIds = AllowedProjectIds,
            Enabled = Enabled ?? true,
            ProjectId = projectId,
            TenantId = tenantId,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            RegressionTestSetId = RegressionTestSetId,
            RegressionEvaluatorConfigVersionId = RegressionEvaluatorConfigVersionId,
        };

        def.EnsureInvariants();
        return def;
    }

    /// <summary>
    /// Snapshot pra um payload a partir de um <see cref="AgentDefinition"/> existente —
    /// usado por edit-draft pra fork-ear o estado vivo do publicado.
    /// </summary>
    public static AgentDraftPayload FromAgentDefinition(AgentDefinition def) => new()
    {
        Name = def.Name,
        Description = def.Description,
        Model = def.Model,
        Provider = def.Provider,
        Instructions = def.Instructions,
        Tools = def.Tools,
        StructuredOutput = def.StructuredOutput,
        OperationalMemory = def.OperationalMemory,
        Middlewares = def.Middlewares,
        FallbackProvider = def.FallbackProvider,
        Resilience = def.Resilience,
        CostBudget = def.CostBudget,
        SkillRefs = def.SkillRefs,
        Metadata = def.Metadata,
        Visibility = def.Visibility,
        AllowedProjectIds = def.AllowedProjectIds,
        Enabled = def.Enabled,
        RegressionTestSetId = def.RegressionTestSetId,
        RegressionEvaluatorConfigVersionId = def.RegressionEvaluatorConfigVersionId,
    };
}
