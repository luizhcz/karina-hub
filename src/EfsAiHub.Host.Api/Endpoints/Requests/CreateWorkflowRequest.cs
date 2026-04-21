using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Host.Api.Models.Requests;

public class CreateWorkflowRequest
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "1.0.0";
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required List<WorkflowAgentReference> Agents { get; init; }
    public List<WorkflowExecutorStep> Executors { get; init; } = [];
    public List<WorkflowEdge> Edges { get; init; } = [];
    public WorkflowConfiguration Configuration { get; init; } = new();
    public Dictionary<string, string> Metadata { get; init; } = [];

    public WorkflowDefinition ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Description = Description,
        Version = Version,
        OrchestrationMode = OrchestrationMode,
        Agents = Agents,
        Executors = Executors,
        Edges = Edges,
        Configuration = Configuration,
        Metadata = Metadata
    };
}
