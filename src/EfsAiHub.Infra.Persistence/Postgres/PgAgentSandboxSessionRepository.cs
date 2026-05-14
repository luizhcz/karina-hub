using EfsAiHub.Core.Abstractions.AgentSandbox;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgAgentSandboxSessionRepository(
    IDbContextFactory<AgentFwDbContext> factory) : IAgentSandboxSessionRepository
{
    public async Task<AgentSandboxSession> CreateAsync(AgentSandboxSession session, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        ctx.AgentSandboxSessions.Add(ToRow(session));
        await ctx.SaveChangesAsync(ct);
        return session;
    }

    public async Task<AgentSandboxSession?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentSandboxSessions.FindAsync([sessionId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<AgentSandboxSession>> ListByAgentAsync(
        string agentId,
        AgentSandboxSessionStatus? statusFilter = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var query = ctx.AgentSandboxSessions
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

    public async Task<AgentSandboxSession> UpdateAsync(AgentSandboxSession session, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentSandboxSessions.FindAsync([session.SandboxSessionId], ct)
            ?? throw new InvalidOperationException(
                $"AgentSandboxSession '{session.SandboxSessionId}' não encontrada pra update.");

        row.LastMessageAt = session.LastMessageAt;
        row.Status = session.Status.ToString();
        row.ValidatedAt = session.ValidatedAt;
        row.ValidatedByUserId = session.ValidatedByUserId;
        row.ValidationNotes = session.ValidationNotes;
        await ctx.SaveChangesAsync(ct);
        return session;
    }

    public async Task<IReadOnlyList<AgentSandboxSession>> ListExpiredAsync(int batchSize, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var rows = await ctx.AgentSandboxSessions
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
        var row = await ctx.AgentSandboxSessions.FindAsync([sessionId], ct);
        if (row is null) return;
        row.Status = "Expired";
        await ctx.SaveChangesAsync(ct);
    }

    private static AgentSandboxSessionRow ToRow(AgentSandboxSession s) => new()
    {
        SandboxSessionId = s.SandboxSessionId,
        AgentId = s.AgentId,
        AgentVersionId = s.AgentVersionId,
        Mode = s.Mode,
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

    private static AgentSandboxSession ToDomain(AgentSandboxSessionRow r) => new()
    {
        SandboxSessionId = r.SandboxSessionId,
        AgentId = r.AgentId,
        AgentVersionId = r.AgentVersionId,
        Mode = r.Mode,
        WorkflowId = r.WorkflowId,
        ConversationId = r.ConversationId,
        ProjectId = r.ProjectId,
        CreatedByUserId = r.CreatedByUserId,
        CreatedAt = r.CreatedAt,
        LastMessageAt = r.LastMessageAt,
        ExpiresAt = r.ExpiresAt,
        Status = Enum.TryParse<AgentSandboxSessionStatus>(r.Status, out var s) ? s : AgentSandboxSessionStatus.Active,
        ValidatedAt = r.ValidatedAt,
        ValidatedByUserId = r.ValidatedByUserId,
        ValidationNotes = r.ValidationNotes,
    };
}
