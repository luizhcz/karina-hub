using EfsAiHub.Core.Abstractions.Conversations;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persiste ConversationSession em PostgreSQL via EF Core.
/// </summary>
public class PgConversationRepository : IConversationRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgConversationRepository> _logger;

    public PgConversationRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgConversationRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<ConversationSession> CreateAsync(ConversationSession session, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Conversations.Add(session);
        await db.SaveChangesAsync(ct);
        _logger.LogInformation("[ConvRepo] Conversa '{ConversationId}' criada para usuário '{UserId}'.",
            session.ConversationId, session.UserId);
        return session;
    }

    public async Task<ConversationSession?> GetByIdAsync(string conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Conversations.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);
    }

    public async Task<IReadOnlyList<ConversationSession>> GetByUserIdAsync(string userId, int limit = 50, CancellationToken ct = default)
    {
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Conversations.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.LastMessageAt)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<ConversationSession> UpdateAsync(ConversationSession session, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.Conversations.Update(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public async Task DeleteAsync(string conversationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var session = await db.Conversations
            .FirstOrDefaultAsync(c => c.ConversationId == conversationId, ct);
        if (session is null) return;
        db.Conversations.Remove(session);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ConversationSession>> GetAllAsync(
        string? userId = null,
        string? workflowId = null,
        string? projectId = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(c => c.UserId == userId);
        if (!string.IsNullOrWhiteSpace(workflowId)) query = query.Where(c => c.WorkflowId == workflowId);
        if (!string.IsNullOrWhiteSpace(projectId)) query = query.Where(c => c.ProjectId == projectId);
        if (from.HasValue) query = query.Where(c => c.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(c => c.CreatedAt <= to.Value);
        return await query
            .OrderByDescending(c => c.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<int> CountAllAsync(
        string? userId = null,
        string? workflowId = null,
        string? projectId = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Conversations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(c => c.UserId == userId);
        if (!string.IsNullOrWhiteSpace(workflowId)) query = query.Where(c => c.WorkflowId == workflowId);
        if (!string.IsNullOrWhiteSpace(projectId)) query = query.Where(c => c.ProjectId == projectId);
        if (from.HasValue) query = query.Where(c => c.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(c => c.CreatedAt <= to.Value);
        return await query.CountAsync(ct);
    }
}
