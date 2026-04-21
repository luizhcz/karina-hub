using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Host.Api.Models.Responses;

public class WorkflowResponse
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required string Version { get; init; }
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required List<WorkflowAgentReference> Agents { get; init; }
    public List<WorkflowExecutorStep> Executors { get; init; } = [];
    public List<WorkflowEdge> Edges { get; init; } = [];
    public WorkflowConfiguration Configuration { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = [];
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
        CreatedAt = def.CreatedAt,
        UpdatedAt = def.UpdatedAt
    };
}
