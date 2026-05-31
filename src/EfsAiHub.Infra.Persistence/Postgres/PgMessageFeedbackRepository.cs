using EfsAiHub.Core.Abstractions.Conversations;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgMessageFeedbackRepository : IMessageFeedbackRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgMessageFeedbackRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<MessageFeedback> UpsertAsync(MessageFeedback feedback, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var existing = await db.MessageFeedbacks
            .FirstOrDefaultAsync(f => f.UserId == feedback.UserId && f.MessageId == feedback.MessageId, ct);

        if (existing is null)
        {
            db.MessageFeedbacks.Add(feedback);
            await db.SaveChangesAsync(ct);
            return feedback;
        }

        existing.Sentiment = feedback.Sentiment;
        existing.Comment = feedback.Comment;
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return existing;
    }

    public async Task<MessageFeedback?> GetByUserAndMessageAsync(
        string userId, string messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MessageFeedbacks.AsNoTracking()
            .FirstOrDefaultAsync(f => f.UserId == userId && f.MessageId == messageId, ct);
    }

    public async Task<bool> DeleteByUserAndMessageAsync(
        string userId, string messageId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.MessageFeedbacks
            .Where(f => f.UserId == userId && f.MessageId == messageId)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    public async Task<IReadOnlyList<MessageFeedback>> ListAdminAsync(
        int? sentiment = null,
        string? conversationId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = BuildAdminQuery(db, sentiment, conversationId, from, to);
        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((Math.Max(1, page) - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAdminAsync(
        int? sentiment = null,
        string? conversationId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await BuildAdminQuery(db, sentiment, conversationId, from, to).CountAsync(ct);
    }

    private static IQueryable<MessageFeedback> BuildAdminQuery(
        AgentFwDbContext db,
        int? sentiment,
        string? conversationId,
        DateTime? from,
        DateTime? to)
    {
        var query = db.MessageFeedbacks.AsNoTracking().AsQueryable();

        if (sentiment.HasValue) query = query.Where(f => f.Sentiment == sentiment.Value);
        if (!string.IsNullOrWhiteSpace(conversationId)) query = query.Where(f => f.ConversationId == conversationId);
        if (from.HasValue) query = query.Where(f => f.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(f => f.CreatedAt <= to.Value);

        return query;
    }
}
