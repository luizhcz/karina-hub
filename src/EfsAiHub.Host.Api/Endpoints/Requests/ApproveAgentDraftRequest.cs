namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body opcional do POST /api/agent-approvals/{id}/approve. ChangeReason vai pra
/// AgentVersion criada no publish (rastreabilidade do snapshot).
/// </summary>
public sealed class ApproveAgentDraftRequest
{
    public string? ChangeReason { get; init; }
}
