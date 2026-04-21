namespace EfsAiHub.Host.Api.Chat.AgUi.Approval;

/// <summary>
/// Payload serializado como Args no TOOL_CALL_ARGS do request_approval.
/// Representa a solicitação de aprovação humana (HITL) seguindo o protocolo AG-UI.
/// </summary>
public sealed record ApprovalRequest(
    string InteractionId,
    string Question,
    string[]? Options = null,
    int? TimeoutSeconds = null);
