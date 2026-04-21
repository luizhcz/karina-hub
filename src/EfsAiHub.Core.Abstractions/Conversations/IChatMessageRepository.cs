namespace EfsAiHub.Core.Abstractions.Conversations;

public interface IChatMessageRepository
{
    Task<ChatMessage> SaveAsync(ChatMessage message, CancellationToken ct = default);

    /// <summary>
    /// Persiste múltiplas mensagens em uma única operação (batch insert).
    /// </summary>
    Task SaveBatchAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default);

    /// <summary>
    /// Lista mensagens de uma conversa, da mais recente para a mais antiga.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> ListAsync(
        string conversationId,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default);

    /// <summary>
    /// Busca as últimas N mensagens com createdAt > sinceUtc (para montar o contexto do ChatTurnContext).
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> GetContextWindowAsync(
        string conversationId,
        int maxMessages,
        DateTime? sinceUtc = null,
        CancellationToken ct = default);

    /// <summary>
    /// Atualiza o TokenCount de uma mensagem existente.
    /// </summary>
    Task UpdateTokenCountAsync(string messageId, int tokenCount, CancellationToken ct = default);

    /// <summary>
    /// Remove todas as mensagens de uma conversa (cascade delete).
    /// </summary>
    Task<int> DeleteByConversationAsync(string conversationId, CancellationToken ct = default);
}
