using System.Text.Json;

namespace EfsAiHub.Core.Abstractions.Conversations;

/// <summary>
/// Mensagem individual persistida no histórico de uma conversa.
/// </summary>
public class ChatMessage
{
    public required string MessageId { get; init; }
    public required string ConversationId { get; init; }

    /// <summary>"user" | "assistant" | "system"</summary>
    public required string Role { get; init; }

    /// <summary>Conteúdo textual da mensagem.</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Output estruturado retornado pelo workflow (apenas role: "assistant").
    /// Null quando a resposta é só texto.
    /// </summary>
    public JsonDocument? StructuredOutput { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Contagem de tokens. Para mensagens assistant, atualizado com valor real de llm_token_usage.</summary>
    public int TokenCount { get; set; }

    /// <summary>ExecutionId que gerou esta mensagem (apenas role: "assistant").</summary>
    public string? ExecutionId { get; init; }
}
