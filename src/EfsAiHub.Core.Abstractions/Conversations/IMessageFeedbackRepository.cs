namespace EfsAiHub.Core.Abstractions.Conversations;

public interface IMessageFeedbackRepository
{
    /// <summary>
    /// Upsert: cria novo registro ou atualiza Sentiment/Comment/UpdatedAt do
    /// feedback existente para o par (UserId, MessageId).
    /// </summary>
    Task<MessageFeedback> UpsertAsync(MessageFeedback feedback, CancellationToken ct = default);

    Task<MessageFeedback?> GetByUserAndMessageAsync(
        string userId, string messageId, CancellationToken ct = default);

    /// <summary>Remove o feedback do usuário para uma mensagem. Retorna true se removeu.</summary>
    Task<bool> DeleteByUserAndMessageAsync(
        string userId, string messageId, CancellationToken ct = default);

    Task<IReadOnlyList<MessageFeedback>> ListAdminAsync(
        int? sentiment = null,
        string? conversationId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<int> CountAdminAsync(
        int? sentiment = null,
        string? conversationId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
