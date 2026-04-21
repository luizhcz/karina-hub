using System.Text.Json;
using System.Text.Json.Serialization;

namespace EfsAiHub.Core.Abstractions.Conversations;

/// <summary>
/// Payload serializado como JSON e enviado ao workflow como WorkflowExecution.Input.
/// Contém a mensagem atual do usuário, o histórico filtrado e metadados da conversa.
/// </summary>
public class ChatTurnContext
{
    public required string UserId { get; init; }
    public required string ConversationId { get; init; }
    public required ChatTurnMessage Message { get; init; }
    public List<ChatTurnMessage> History { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Snapshot do AG-UI shared state da conversa (agent drafts).
    /// Null quando não há state ou a execução não é de Chat.
    /// Injetado pelo ConversationService ao montar o contexto do turno.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? SharedState { get; init; }
}

/// <summary>
/// Uma mensagem no contexto do turno (mensagem atual ou histórico).
/// </summary>
public class ChatTurnMessage
{
    /// <summary>"user" | "assistant" | "system"</summary>
    public required string Role { get; init; }

    public required string Content { get; init; }

    /// <summary>
    /// Output estruturado de mensagens assistente anteriores.
    /// Null para mensagens de usuário ou quando não há output estruturado.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Output { get; init; }
}
