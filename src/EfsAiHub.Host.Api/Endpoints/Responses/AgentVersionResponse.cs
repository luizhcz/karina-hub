using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// DTO de snapshot imutável de AgentVersion. Inclui todos os campos
/// versionados (prompt, model, tools, middlewares, resilience, cost budget, skills)
/// + metadados de auditoria (<c>ContentHash</c>, <c>Revision</c>, <c>CreatedBy</c>).
/// </summary>
public class AgentVersionResponse
{
    public required string AgentVersionId { get; init; }
    public required string AgentDefinitionId { get; init; }
    public required int Revision { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ChangeReason { get; init; }
    public string? PromptContent { get; init; }
    public string? PromptVersionId { get; init; }
    public AgentModelSnapshot? Model { get; init; }
    public AgentProviderSnapshot? Provider { get; init; }
    public IReadOnlyList<ToolFingerprint> ToolFingerprints { get; init; } = [];
    public IReadOnlyList<AgentMiddlewareSnapshot> MiddlewarePipeline { get; init; } = [];
    public AgentStructuredOutputSnapshot? OutputSchema { get; init; }
    public ResiliencePolicy? Resilience { get; init; }
    public AgentCostBudget? CostBudget { get; init; }
    public IReadOnlyList<SkillRef> SkillRefs { get; init; } = [];
    public required string ContentHash { get; init; }

    public static AgentVersionResponse FromDomain(AgentVersion v) => new()
    {
        AgentVersionId = v.AgentVersionId,
        AgentDefinitionId = v.AgentDefinitionId,
        Revision = v.Revision,
        Status = v.Status.ToString(),
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy,
        ChangeReason = v.ChangeReason,
        PromptContent = v.PromptContent,
        PromptVersionId = v.PromptVersionId,
        Model = v.Model,
        Provider = v.Provider,
        ToolFingerprints = v.ToolFingerprints,
        MiddlewarePipeline = v.MiddlewarePipeline,
        OutputSchema = v.OutputSchema,
        Resilience = v.Resilience,
        CostBudget = v.CostBudget,
        SkillRefs = v.SkillRefs,
        ContentHash = v.ContentHash
    };
}

/// <summary>Body opcional para /rollback.</summary>
public class RollbackAgentRequest
{
    public required string TargetVersionId { get; init; }
    public string? ChangeReason { get; init; }
}
