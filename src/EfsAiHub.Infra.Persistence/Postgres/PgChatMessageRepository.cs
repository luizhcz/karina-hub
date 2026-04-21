using EfsAiHub.Core.Abstractions.Conversations;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persiste ChatMessage em PostgreSQL via EF Core.
/// Append-only: mensagens nunca são editadas ou removidas individualmente.
/// </summary>
public class PgChatMessageRepository : IChatMessageRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgChatMessageRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<ChatMessage> SaveAsync(ChatMessage message, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync(ct);
        return message;
    }

    public async Task SaveBatchAsync(IReadOnlyList<ChatMessage> messages, CancellationToken ct = default)
    {
        if (messages.Count == 0) return;
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ChatMessages.AddRange(messages);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> ListAsync(
        string conversationId,
        int limit = 50,
        int offset = 0,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task UpdateTokenCountAsync(string messageId, int tokenCount, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await db.ChatMessages
            .Where(m => m.MessageId == messageId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TokenCount, tokenCount), ct);
    }

    public async Task<int> DeleteByConversationAsync(string conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ChatMessages
            .Where(m => m.ConversationId == conversationId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<ChatMessage>> GetContextWindowAsync(
        string conversationId,
        int maxMessages,
        DateTime? sinceUtc = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId);

        if (sinceUtc.HasValue)
            query = query.Where(m => m.CreatedAt > sinceUtc.Value);

        // Busca as N mensagens mais recentes em ordem crescente (mais antigas primeiro) para o contexto do LLM
        return await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxMessages)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);
    }
}
