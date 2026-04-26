namespace EfsAiHub.Host.Api.Chat.AgUi.Models;

/// <summary>
/// Mensagem de entrada enviada pelo frontend no request AG-UI.
/// Suporta role=tool para respostas de aprovação HITL (request_approval).
///
/// <para>
/// <see cref="Actor"/> é extensão proprietária aditiva à spec AG-UI (que define só
/// role nos 5 canônicos). Valores aceitos: <c>"human"</c> (default quando null),
/// <c>"robot"</c>. Quando a última mensagem do batch tem <see cref="Actor"/>=robot,
/// o backend persiste sem disparar workflow. Ver ADR 0014.
/// </para>
/// </summary>
public sealed record AgUiInputMessage(
    string Role,
    string Content,
    string? ToolCallId = null,
    string? Actor = null);
