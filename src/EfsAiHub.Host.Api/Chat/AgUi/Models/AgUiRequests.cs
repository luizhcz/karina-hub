namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

public sealed record CancelRunRequest(string ExecutionId);

/// <summary>
/// Payload para resolver uma interação HITL pendente via POST /resolve-hitl.
/// ToolCallId = interactionId emitido no TOOL_CALL_START(request_approval).
/// Response = "approved" | "rejected" (ou texto livre — o tool interpreta).
/// </summary>
public sealed record HitlResolveRequest(string ToolCallId, string Response);
