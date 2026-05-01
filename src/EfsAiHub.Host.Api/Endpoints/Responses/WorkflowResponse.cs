using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Host.Api.Models.Responses;

public class WorkflowResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required IReadOnlyList<WorkflowAgentReference> Agents { get; init; }
    public IReadOnlyList<WorkflowExecutorStep> Executors { get; init; } = [];
    public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];
    public WorkflowConfiguration Configuration { get; init; } = new();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    /// <summary>"project" (default) | "global"</summary>
    public required string Visibility { get; init; }
    /// <summary>Project owner do workflow. Distingue de quem está consumindo (caller pode ser outro).</summary>
    public required string OriginProjectId { get; init; }
    /// <summary>Tenant do owner. Reforça boundary cross-tenant na UI.</summary>
    public required string OriginTenantId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static WorkflowResponse FromDomain(WorkflowDefinition def) => new()
    {
        Id = def.Id,
        Name = def.Name,
        Description = def.Description,
        Version = def.Version,
        OrchestrationMode = def.OrchestrationMode,
        Agents = def.Agents,
        Executors = def.Executors,
        Edges = def.Edges,
        Configuration = def.Configuration,
        Metadata = def.Metadata,
        Visibility = def.Visibility,
        OriginProjectId = def.ProjectId,
        OriginTenantId = def.TenantId,
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt
    };
}
