using System.Text.Json;
using EfsAiHub.Core.Agents;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Armazena AgentSessionRecord em PostgreSQL com TTL de 30 dias via coluna ExpiresAt.
/// Linhas expiradas são limpas periodicamente pelo AgentSessionCleanupService.
/// </summary>
public class PgAgentSessionStore(
    IDbContextFactory<AgentFwDbContext> factory,
    ILogger<PgAgentSessionStore> logger) : IAgentSessionStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    public async Task<AgentSessionRecord> CreateAsync(AgentSessionRecord record, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        ctx.AgentSessions.Add(new AgentSessionRow
        {
            SessionId = record.SessionId,
            AgentId = record.AgentId,
            SerializedState = JsonSerializer.Serialize(record.SerializedState),
            TurnCount = record.TurnCount,
            CreatedAt = record.CreatedAt,
            LastAccessedAt = record.LastAccessedAt,
            ExpiresAt = DateTime.UtcNow.Add(Ttl)
        });
        await ctx.SaveChangesAsync(ct);

        logger.LogDebug("[SessionStore] Sessão '{SessionId}' criada para agente '{AgentId}'.",
            record.SessionId, record.AgentId);
        return record;
    }

    public async Task<AgentSessionRecord?> GetByIdAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentSessions.FindAsync([sessionId], ct);
        if (row is null || row.ExpiresAt <= DateTime.UtcNow) return null;
        return ToRecord(row);
    }

    public async Task<IReadOnlyList<AgentSessionRecord>> GetByAgentIdAsync(string agentId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        return await ctx.AgentSessions
            .AsNoTracking()
            .Where(r => r.AgentId == agentId && r.ExpiresAt > now)
            .OrderByDescending(r => r.LastAccessedAt)
            .Select(r => ToRecord(r))
            .ToListAsync(ct);
    }

    public async Task<AgentSessionRecord> UpdateAsync(AgentSessionRecord record, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentSessions.FindAsync([record.SessionId], ct);
        if (row is null)
        {
            return await CreateAsync(record, ct);
        }

        row.SerializedState = JsonSerializer.Serialize(record.SerializedState);
        row.TurnCount = record.TurnCount;
        row.LastAccessedAt = record.LastAccessedAt;
        row.ExpiresAt = DateTime.UtcNow.Add(Ttl);
        await ctx.SaveChangesAsync(ct);
        return record;
    }

    public async Task<bool> DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.AgentSessions.FindAsync([sessionId], ct);
        if (row is null) return false;

        ctx.AgentSessions.Remove(row);
        await ctx.SaveChangesAsync(ct);

        logger.LogDebug("[SessionStore] Sessão '{SessionId}' removida.", sessionId);
        return true;
    }

    private static AgentSessionRecord ToRecord(AgentSessionRow row) => new()
    {
        SessionId = row.SessionId,
        AgentId = row.AgentId,
        SerializedState = JsonSerializer.Deserialize<JsonElement>(row.SerializedState),
        TurnCount = row.TurnCount,
        CreatedAt = row.CreatedAt,
        LastAccessedAt = row.LastAccessedAt
    };
}
