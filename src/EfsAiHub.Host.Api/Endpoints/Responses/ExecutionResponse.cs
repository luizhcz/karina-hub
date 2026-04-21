using EfsAiHub.Core.Orchestration.Enums;

namespace EfsAiHub.Host.Api.Models.Responses;

public class ExecutionResponse
{
    public required string ExecutionId { get; init; }
    public required string WorkflowId { get; init; }
    public required WorkflowStatus Status { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public IDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    public static ExecutionResponse FromDomain(WorkflowExecution ex) => new()
    {
        ExecutionId = ex.ExecutionId,
        WorkflowId = ex.WorkflowId,
        Status = ex.Status,
        Input = ex.Input,
        Output = ex.Output,
        ErrorMessage = ex.ErrorMessage,
        StartedAt = ex.StartedAt,
        CompletedAt = ex.CompletedAt,
        Metadata = ex.Metadata
    };
}

public class ExecutionDetailResponse : ExecutionResponse
{
    public List<ExecutionStepResponse> Steps { get; init; } = [];

    public new static ExecutionDetailResponse FromDomain(WorkflowExecution ex) => new()
    {
        ExecutionId = ex.ExecutionId,
        WorkflowId = ex.WorkflowId,
        Status = ex.Status,
        Input = ex.Input,
        Output = ex.Output,
        ErrorMessage = ex.ErrorMessage,
        StartedAt = ex.StartedAt,
        CompletedAt = ex.CompletedAt,
        Metadata = ex.Metadata,
        Steps = ex.Steps.Select(ExecutionStepResponse.FromDomain).ToList()
    };
}

public class ExecutionStepResponse
{
    public required string StepId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public required WorkflowStatus Status { get; init; }
    public string? Output { get; init; }
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int TokensUsed { get; init; }

    public static ExecutionStepResponse FromDomain(ExecutionStep step) => new()
    {
        StepId = step.StepId,
        AgentId = step.AgentId,
        AgentName = step.AgentName,
        Status = step.Status,
        Output = step.Output,
        StartedAt = step.StartedAt,
        CompletedAt = step.CompletedAt,
        TokensUsed = step.TokensUsed
    };
}
