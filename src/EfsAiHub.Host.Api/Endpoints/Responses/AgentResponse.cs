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
    public IReadOnlyList<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public IReadOnlyList<AgentMiddlewareConfig> Middlewares { get; init; } = [];

    /// <summary>Fase 2.</summary>
    public ResiliencePolicy? Resilience { get; init; }

    /// <summary>Fase 2.</summary>
    public AgentCostBudget? CostBudget { get; init; }

    /// <summary>Fase 3.</summary>
    public IReadOnlyList<SkillRef>? SkillRefs { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>Phase 2 — "project" (default) | "global".</summary>
    public required string Visibility { get; init; }

    /// <summary>Phase 2 — Project owner do agent (distingue do caller que está consumindo).</summary>
    public required string OriginProjectId { get; init; }

    /// <summary>Phase 2 — Tenant do owner. Reforça boundary cross-tenant na UI.</summary>
    public required string OriginTenantId { get; init; }

    /// <summary>
    /// Phase 3 — Whitelist opcional de projetos autorizados a referenciar quando
    /// Visibility=global. Null = qualquer projeto do tenant pode.
    /// </summary>
    public IReadOnlyList<string>? AllowedProjectIds { get; init; }

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
        Visibility = def.Visibility,
        OriginProjectId = def.ProjectId,
        OriginTenantId = def.TenantId,
        AllowedProjectIds = def.AllowedProjectIds,
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt
    };
}
