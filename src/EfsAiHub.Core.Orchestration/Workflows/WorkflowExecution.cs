using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Core.Orchestration.Workflows;

public class WorkflowExecution : EfsAiHub.Core.Abstractions.Persistence.IProjectScoped
{
    public required string ExecutionId { get; init; }
    public required string WorkflowId { get; init; }
    public string ProjectId { get; init; } = "default";
    public WorkflowStatus Status { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public ErrorCategory? ErrorCategory { get; set; }
    [JsonIgnore]
    public List<ExecutionStep> Steps { get; init; } = [];
    public string? CheckpointKey { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ConcurrentDictionary<string, string> Metadata { get; init; } = new();
}

public class ExecutionStep
{
    public required string StepId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public WorkflowStatus Status { get; set; }
    public string? Input { get; init; }
    public string? Output { get; set; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public int TokensUsed { get; set; }
}
