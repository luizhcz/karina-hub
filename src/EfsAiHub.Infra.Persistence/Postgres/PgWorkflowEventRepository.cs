using EfsAiHub.Core.Orchestration.Workflows;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persiste WorkflowEventEnvelope em PostgreSQL como audit trail.
/// Persiste eventos de workflow para replay SSE e auditoria (TTL 7d via AgentSessionCleanupService).
/// Limpeza de registros antigos (> 7 dias) é feita pelo AgentSessionCleanupService.
/// </summary>
public class PgWorkflowEventRepository(IDbContextFactory<AgentFwDbContext> factory) : IWorkflowEventRepository
{
    public async Task<long> AppendAsync(WorkflowEventEnvelope envelope, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = new WorkflowEventAuditRow
        {
            ExecutionId = envelope.ExecutionId,
            EventType = envelope.EventType,
            Payload = envelope.Payload,
            Timestamp = envelope.Timestamp
        };
        ctx.WorkflowEventAudits.Add(row);
        await ctx.SaveChangesAsync(ct);
        return row.Id; // EF Core popula o Id auto-gerado após SaveChanges
    }

    public async Task<IReadOnlyList<WorkflowEventEnvelope>> GetAllAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        return await ctx.WorkflowEventAudits
            .AsNoTracking()
            .Where(r => r.ExecutionId == executionId)
            .OrderBy(r => r.Id)
            .Select(r => new WorkflowEventEnvelope
            {
                ExecutionId = r.ExecutionId,
                EventType = r.EventType,
                Payload = r.Payload,
                Timestamp = r.Timestamp,
                SequenceId = r.Id
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<WorkflowEventEnvelope>>> GetAllByExecutionIdsAsync(
        IEnumerable<string> executionIds, CancellationToken ct = default)
    {
        var idList = executionIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<string, IReadOnlyList<WorkflowEventEnvelope>>();

        await using var ctx = await factory.CreateDbContextAsync(ct);
        var rows = await ctx.WorkflowEventAudits
            .AsNoTracking()
            .Where(r => idList.Contains(r.ExecutionId))
            .OrderBy(r => r.Id)
            .Select(r => new WorkflowEventEnvelope
            {
                ExecutionId = r.ExecutionId,
                EventType = r.EventType,
                Payload = r.Payload,
                Timestamp = r.Timestamp,
                SequenceId = r.Id
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(e => e.ExecutionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<WorkflowEventEnvelope>)g.ToList());
    }

    public async Task<WorkflowEventEnvelope?> GetBySequenceIdAsync(long sequenceId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowEventAudits
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == sequenceId, ct);

        if (row is null) return null;

        return new WorkflowEventEnvelope
        {
            ExecutionId = row.ExecutionId,
            EventType = row.EventType,
            Payload = row.Payload,
            Timestamp = row.Timestamp,
            SequenceId = row.Id
        };
    }
}
