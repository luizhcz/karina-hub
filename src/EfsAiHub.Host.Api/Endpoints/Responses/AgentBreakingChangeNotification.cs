namespace EfsAiHub.Host.Api.Models.Responses;

/// <summary>
/// Item da lista retornada por <c>GET /api/notifications/agent-breaking-changes</c>.
/// Cada entry representa uma <c>AgentVersion</c> com <c>BreakingChange=true</c>
/// publicada recentemente — sinaliza pra UI mostrar bell + dropdown com workflows
/// que precisam de revisão de pin.
/// </summary>
public class AgentBreakingChangeNotification
{
    public required string AgentId { get; init; }
    public string? AgentName { get; init; }
    public required string AgentVersionId { get; init; }
    public required int Revision { get; init; }
    public string? ChangeReason { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}
