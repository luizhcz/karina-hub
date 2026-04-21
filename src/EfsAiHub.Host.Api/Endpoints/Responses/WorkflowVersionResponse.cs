using EfsAiHub.Core.Orchestration.Workflows;

namespace EfsAiHub.Host.Api.Models.Responses;

public class WorkflowVersionResponse
{
    public required string WorkflowVersionId { get; init; }
    public required string WorkflowDefinitionId { get; init; }
    public required int Revision { get; init; }
    public required string Status { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ChangeReason { get; init; }
    public required string ContentHash { get; init; }

    public static WorkflowVersionResponse FromDomain(WorkflowVersion v) => new()
    {
        WorkflowVersionId = v.WorkflowVersionId,
        WorkflowDefinitionId = v.WorkflowDefinitionId,
        Revision = v.Revision,
        Status = v.Status.ToString(),
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy,
        ChangeReason = v.ChangeReason,
        ContentHash = v.ContentHash
    };
}

public class RollbackWorkflowRequest
{
    public required string VersionId { get; init; }
}

public class CloneWorkflowRequest
{
    public string? NewId { get; init; }
}
