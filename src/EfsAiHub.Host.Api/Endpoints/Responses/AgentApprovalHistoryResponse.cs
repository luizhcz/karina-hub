using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Api.Models.Responses;

public sealed class AgentApprovalHistoryResponse
{
    public required string Id { get; init; }
    public required string DraftId { get; init; }
    public string? AgentDefinitionId { get; init; }
    public required string Action { get; init; }
    public required string ActorUserId { get; init; }
    public string? Feedback { get; init; }
    public string? Tier { get; init; }
    public DateTime OccurredAt { get; init; }

    public static AgentApprovalHistoryResponse FromDomain(AgentApprovalHistoryEntry e) => new()
    {
        Id = e.Id,
        DraftId = e.DraftId,
        AgentDefinitionId = e.AgentDefinitionId,
        Action = e.Action.ToString(),
        ActorUserId = e.ActorUserId,
        Feedback = e.Feedback,
        Tier = e.Tier,
        OccurredAt = e.OccurredAt,
    };
}
