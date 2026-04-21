namespace EfsAiHub.Core.Abstractions.Conversations;

public interface IConversationRepository
{
    Task<ConversationSession> CreateAsync(ConversationSession session, CancellationToken ct = default);
    Task<ConversationSession?> GetByIdAsync(string conversationId, CancellationToken ct = default);
    Task<IReadOnlyList<ConversationSession>> GetByUserIdAsync(string userId, int limit = 50, CancellationToken ct = default);
    Task<ConversationSession> UpdateAsync(ConversationSession session, CancellationToken ct = default);
    Task DeleteAsync(string conversationId, CancellationToken ct = default);

    /// <summary>Admin: lista todas as conversas com filtros opcionais e paginação.</summary>
    Task<IReadOnlyList<ConversationSession>> GetAllAsync(
        string? userId = null,
        string? workflowId = null,
        string? projectId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default);

    Task<int> CountAllAsync(
        string? userId = null,
        string? workflowId = null,
        string? projectId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default);
}
