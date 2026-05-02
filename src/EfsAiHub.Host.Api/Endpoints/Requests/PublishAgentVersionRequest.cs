namespace EfsAiHub.Host.Api.Models.Requests;

/// <summary>
/// Body do endpoint <c>POST /api/agents/{id}/versions</c>. Caller declara intent
/// de breaking explicitamente. <c>BreakingChange=true</c> exige <c>ChangeReason</c>
/// não-vazio (validado em <c>AgentVersion.EnsureInvariants</c>).
/// </summary>
public class PublishAgentVersionRequest
{
    public required bool BreakingChange { get; init; }
    public string? ChangeReason { get; init; }
}
