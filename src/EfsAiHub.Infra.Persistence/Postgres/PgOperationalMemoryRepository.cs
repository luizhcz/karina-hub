using System.Text.Json;
using EfsAiHub.Core.Agents;
using EfsAiHub.Core.Agents.Memory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgOperationalMemoryRepository : IOperationalMemoryRepository
{
    // Postgres SQLSTATE 23505 = unique_violation. Acontece quando duas inserts
    // colidem na PK composta — a primeira ganha e a segunda joga aqui.
    private const string UniqueViolationSqlState = "23505";

    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgOperationalMemoryRepository> _logger;

    public PgOperationalMemoryRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgOperationalMemoryRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<OperationalMemoryRecord?> GetAsync(
        string agentId,
        string scopeType,
        string scopeId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.OperationalMemory.FirstOrDefaultAsync(
            r => r.AgentId == agentId && r.ScopeType == scopeType && r.ScopeId == scopeId,
            ct);
        return row is null ? null : Hydrate(row);
    }

    public async Task<IReadOnlyList<OperationalMemoryRecord>> ListAsync(
        string agentId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.OperationalMemory
            .Where(r => r.AgentId == agentId)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(Hydrate).ToList();
    }

    public async Task<OperationalMemoryRecord> UpsertAsync(
        OperationalMemoryRecord record,
        int? expectedVersion,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var payloadJson = record.Payload.GetRawText();

        // Tenta UPDATE com filtro opcional por Version. ExecuteUpdateAsync
        // honra o HasQueryFilter por ProjectId, então memória de outro project
        // jamais é atingida mesmo com chave casando.
        var query = ctx.OperationalMemory.Where(r =>
            r.AgentId == record.AgentId
            && r.ScopeType == record.ScopeType
            && r.ScopeId == record.ScopeId);

        if (expectedVersion.HasValue)
        {
            var v = expectedVersion.Value;
            query = query.Where(r => r.Version == v);
        }

        var affected = await query
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Payload, payloadJson)
                .SetProperty(r => r.Version, r => r.Version + 1)
                .SetProperty(r => r.UpdatedAt, now),
                ct);

        if (affected > 0)
        {
            // Re-leitura pra devolver o estado autoritativo (CreatedAt original
            // + Version bumped pelo banco).
            var fresh = await ctx.OperationalMemory.FirstAsync(
                r => r.AgentId == record.AgentId
                  && r.ScopeType == record.ScopeType
                  && r.ScopeId == record.ScopeId,
                ct);
            return Hydrate(fresh);
        }

        // 0 rows afetadas. Caminhos possíveis:
        //  - expectedVersion era null/0: row inexistente → tentar INSERT.
        //  - expectedVersion era N≥1: ou a row sumiu ou Version divergiu → conflict.
        var isInitialWrite = expectedVersion is null or 0;
        if (!isInitialWrite)
            throw new OperationalMemoryConcurrencyException(
                record.AgentId, record.ScopeType, record.ScopeId);

        var newRow = new OperationalMemoryRow
        {
            ProjectId = record.ProjectId,
            AgentId = record.AgentId,
            ScopeType = record.ScopeType,
            ScopeId = record.ScopeId,
            Payload = payloadJson,
            Version = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };

        ctx.OperationalMemory.Add(newRow);

        try
        {
            await ctx.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race: outro processo inseriu primeiro entre o UPDATE e o INSERT.
            // Tratamos como conflict — caller decide se relê e tenta de novo.
            throw new OperationalMemoryConcurrencyException(
                record.AgentId, record.ScopeType, record.ScopeId);
        }

        return Hydrate(newRow);
    }

    public async Task<bool> DeleteAsync(
        string agentId,
        string scopeType,
        string scopeId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.OperationalMemory
            .Where(r => r.AgentId == agentId
                     && r.ScopeType == scopeType
                     && r.ScopeId == scopeId)
            .ExecuteDeleteAsync(ct);
        return rows > 0;
    }

    private static OperationalMemoryRecord Hydrate(OperationalMemoryRow row)
    {
        // Parse + Clone destaca o JsonElement do JsonDocument original — devolve
        // memória pro pool no using e o caller pode armazenar/serializar o
        // payload sem precisar disposar nada.
        var raw = string.IsNullOrWhiteSpace(row.Payload) ? "{}" : row.Payload;
        using var doc = JsonDocument.Parse(raw);
        return new OperationalMemoryRecord
        {
            ProjectId = row.ProjectId,
            AgentId = row.AgentId,
            ScopeType = row.ScopeType,
            ScopeId = row.ScopeId,
            Payload = doc.RootElement.Clone(),
            Version = row.Version,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt,
        };
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState;
}
