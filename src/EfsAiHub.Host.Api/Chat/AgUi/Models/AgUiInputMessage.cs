namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

/// <summary>
/// Mensagem de entrada enviada pelo frontend no request AG-UI.
/// Suporta role=tool para respostas de aprovação HITL (request_approval).
/// </summary>
public sealed record AgUiInputMessage(
    string Role,
    string Content,
    string? ToolCallId = null);
