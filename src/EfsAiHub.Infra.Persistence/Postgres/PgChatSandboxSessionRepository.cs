using EfsAiHub.Core.Abstractions.ChatSandbox;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgChatSandboxSessionRepository(
    IDbContextFactory<AgentFwDbContext> factory) : IChatSandboxSessionRepository
{
    public async Task<ChatSandboxSession> CreateAsync(ChatSandboxSession session, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        ctx.ChatSandboxSessions.Add(ToRow(session));
        await ctx.SaveChangesAsync(ct);
        return session;
    }

    public async Task<ChatSandboxSession?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.ChatSandboxSessions.FindAsync([sessionId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<ChatSandboxSession>> ListByAgentAsync(
        string agentId,
        ChatSandboxSessionStatus? statusFilter = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var query = ctx.ChatSandboxSessions
            .AsNoTracking()
            .Where(r => r.AgentId == agentId);

        if (statusFilter.HasValue)
        {
            var statusStr = statusFilter.Value.ToString();
            query = query.Where(r => r.Status == statusStr);
        }

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task<ChatSandboxSession> UpdateAsync(ChatSandboxSession session, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.ChatSandboxSessions.FindAsync([session.ChatSandboxSessionId], ct)
            ?? throw new InvalidOperationException(
                $"ChatSandboxSession '{session.ChatSandboxSessionId}' não encontrada pra update.");

        row.LastMessageAt = session.LastMessageAt;
        row.Status = session.Status.ToString();
        row.ValidatedAt = session.ValidatedAt;
        row.ValidatedByUserId = session.ValidatedByUserId;
        row.ValidationNotes = session.ValidationNotes;
        await ctx.SaveChangesAsync(ct);
        return session;
    }

    public async Task<IReadOnlyList<ChatSandboxSession>> ListExpiredAsync(int batchSize, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var rows = await ctx.ChatSandboxSessions
            .AsNoTracking()
            .Where(r => r.ExpiresAt < now && (r.Status == "Active" || r.Status == "Closed"))
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct);

        return rows.Select(ToDomain).ToList();
    }

    public async Task MarkExpiredAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.ChatSandboxSessions.FindAsync([sessionId], ct);
        if (row is null) return;
        row.Status = "Expired";
        await ctx.SaveChangesAsync(ct);
    }

    private static ChatSandboxSessionRow ToRow(ChatSandboxSession s) => new()
    {
        ChatSandboxSessionId = s.ChatSandboxSessionId,
        AgentId = s.AgentId,
        AgentVersionId = s.AgentVersionId,
        WorkflowId = s.WorkflowId,
        ConversationId = s.ConversationId,
        ProjectId = s.ProjectId,
        CreatedByUserId = s.CreatedByUserId,
        CreatedAt = s.CreatedAt,
        LastMessageAt = s.LastMessageAt,
        ExpiresAt = s.ExpiresAt,
        Status = s.Status.ToString(),
        ValidatedAt = s.ValidatedAt,
        ValidatedByUserId = s.ValidatedByUserId,
        ValidationNotes = s.ValidationNotes,
    };

    private static ChatSandboxSession ToDomain(ChatSandboxSessionRow r) => new()
    {
        ChatSandboxSessionId = r.ChatSandboxSessionId,
        AgentId = r.AgentId,
        AgentVersionId = r.AgentVersionId,
        WorkflowId = r.WorkflowId,
        ConversationId = r.ConversationId,
        ProjectId = r.ProjectId,
        CreatedByUserId = r.CreatedByUserId,
        CreatedAt = r.CreatedAt,
        LastMessageAt = r.LastMessageAt,
        ExpiresAt = r.ExpiresAt,
        Status = Enum.TryParse<ChatSandboxSessionStatus>(r.Status, out var s) ? s : ChatSandboxSessionStatus.Active,
        ValidatedAt = r.ValidatedAt,
        ValidatedByUserId = r.ValidatedByUserId,
        ValidationNotes = r.ValidationNotes,
    };
}
