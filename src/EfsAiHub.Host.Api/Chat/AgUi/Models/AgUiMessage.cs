namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

/// <summary>
/// Mensagem renderizada em <c>MESSAGES_SNAPSHOT</c> (resync e short-circuit robot).
/// <c>Actor</c> é a extensão proprietária aditiva à spec AG-UI (ver ADR 0014):
/// quando ausente, cliente assume <c>"human"</c>; quando <c>"robot"</c>, frontend
/// renderiza bubble distinto. Necessário no snapshot pra clientes externos
/// (CopilotKit, Mastra) e cenário de reconexão sem REST followup.
/// </summary>
public sealed record AgUiMessage(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    string? Actor = null);
