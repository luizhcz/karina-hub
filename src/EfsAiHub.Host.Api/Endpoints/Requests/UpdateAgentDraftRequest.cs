using EfsAiHub.Core.Agents;

namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do PUT /api/aihub/agent-drafts/{id}. <see cref="ExpectedUpdatedAt"/> vem do
/// GET anterior — divergência retorna 412 Precondition Failed (optimistic concurrency).
/// </summary>
public sealed class UpdateAgentDraftRequest
{
    public required AgentDraftPayload Payload { get; init; }

    public required DateTime ExpectedUpdatedAt { get; init; }
}
