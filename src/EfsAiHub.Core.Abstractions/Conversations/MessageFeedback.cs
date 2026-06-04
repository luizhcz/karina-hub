namespace EfsAiHub.Core.Abstractions.Conversations;

/// <summary>
/// Feedback do usuário sobre uma mensagem de assistente.
/// Append-only com upsert por (UserId, MessageId): um feedback por usuário por mensagem;
/// mudar o sentiment ou comment atualiza o registro existente.
/// </summary>
public class MessageFeedback
{
    public required string FeedbackId { get; init; }
    public required string MessageId { get; init; }
    public required string ConversationId { get; init; }
    public required string UserId { get; init; }

    /// <summary>+1 = like, -1 = dislike. Outros valores são rejeitados na facade.</summary>
    public int Sentiment { get; set; }

    /// <summary>Feedback textual opcional do usuário.</summary>
    public string? Comment { get; set; }

    public string ProjectId { get; set; } = "default";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
