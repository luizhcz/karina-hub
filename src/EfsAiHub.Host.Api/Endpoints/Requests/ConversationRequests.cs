namespace EfsAiHub.Host.Api.Models.Requests;

public record CreateConversationRequest(string? WorkflowId = null, Dictionary<string, string>? Metadata = null);

public record ChatMessageInputDto(string Role, string Message);

/// <summary>
/// Body do POST/PUT em /messages/{messageId}/feedback.
/// Sentiment: +1 (like) | -1 (dislike) — outros valores são rejeitados.
/// </summary>
public record SubmitMessageFeedbackRequest(int Sentiment, string? Comment = null);
