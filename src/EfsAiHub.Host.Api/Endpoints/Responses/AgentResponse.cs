using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Skills;

namespace EfsAiHub.Host.Api.Models.Responses;

public class AgentResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required AgentModelConfig Model { get; init; }
    public AgentProviderConfig Provider { get; init; } = new();
    public string? Instructions { get; init; }
    public List<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public List<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    /// <summary>Fase 2.</summary>
    public ResiliencePolicy? Resilience { get; init; }

    /// <summary>Fase 2.</summary>
    public AgentCostBudget? CostBudget { get; init; }

    /// <summary>Fase 3.</summary>
    public List<SkillRef>? SkillRefs { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static AgentResponse FromDomain(AgentDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Model = def.Model,
        Provider = def.Provider,
        Instructions = def.Instructions,
        Tools = def.Tools,
        StructuredOutput = def.StructuredOutput,
        Middlewares = def.Middlewares,
        Resilience = def.Resilience,
        CostBudget = def.CostBudget,
        SkillRefs = def.SkillRefs is { Count: > 0 } ? def.SkillRefs : null,
        Metadata = def.Metadata,
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt
    };
}
