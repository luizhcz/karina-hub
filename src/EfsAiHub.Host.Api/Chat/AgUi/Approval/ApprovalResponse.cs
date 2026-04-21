namespace EfsAiHub.Host.Api.Chat.AgUi.Approval;

/// <summary>
/// Resposta de aprovação humana recebida via mensagem role=tool no próximo request AG-UI.
/// </summary>
public sealed record ApprovalResponse(
    string InteractionId,
    bool Approved,
    string Response);
