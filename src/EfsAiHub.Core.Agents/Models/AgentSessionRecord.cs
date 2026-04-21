using System.Text.Json;

namespace EfsAiHub.Core.Agents;

/// <summary>
/// Registro de uma sessão de conversa com um agente.
/// O estado serializado (JsonElement) é opaco — contém o histórico de mensagens
/// e o StateBag interno do AgentSession do Microsoft Agent Framework.
/// </summary>
public class AgentSessionRecord
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }

    /// <summary>
    /// Estado serializado via agent.SerializeSession(session).
    /// Deve ser restaurado com o mesmo agente/provider que o criou.
    /// </summary>
    public JsonElement SerializedState { get; set; }

    public int TurnCount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
}
