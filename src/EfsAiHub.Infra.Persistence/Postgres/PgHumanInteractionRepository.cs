using System.Text.Json;
using EfsAiHub.Core.Orchestration.Enums;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Repositório PostgreSQL para HumanInteractionRequest.
/// Persiste o estado HITL — decisões e contexto sobrevivem a restarts do processo.
/// </summary>
public class PgHumanInteractionRepository(IDbContextFactory<AgentFwDbContext> factory)
    : IHumanInteractionRepository
{
    public async Task CreateAsync(HumanInteractionRequest request, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        ctx.HumanInteractions.Add(new HumanInteractionRow
        {
            InteractionId = request.InteractionId,
            ExecutionId = request.ExecutionId,
            WorkflowId = request.WorkflowId,
            Prompt = request.Prompt,
            Context = request.Context,
            InteractionType = request.InteractionType.ToString(),
            Options = request.Options is { Count: > 0 }
                ? JsonSerializer.Serialize(request.Options)
                : null,
            Status = request.Status.ToString(),
            Resolution = request.Resolution,
            CreatedAt = request.CreatedAt,
            ResolvedAt = request.ResolvedAt
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HumanInteractionRequest request, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.HumanInteractions.FindAsync([request.InteractionId], ct);
        if (row is null) return;
        row.Status = request.Status.ToString();
        row.Resolution = request.Resolution;
        row.ResolvedAt = request.ResolvedAt;
        row.ResolvedBy = request.ResolvedBy;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HumanInteractionRequest>> GetPendingAsync(CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var rows = await ctx.HumanInteractions
            .AsNoTracking()
            .Where(r => r.Status == nameof(HumanInteractionStatus.Pending))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<HumanInteractionRequest?> GetByIdAsync(string interactionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.HumanInteractions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.InteractionId == interactionId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<HumanInteractionRequest>> GetByExecutionIdAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var rows = await ctx.HumanInteractions.AsNoTracking()
            .Where(r => r.ExecutionId == executionId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<HumanInteractionRequest?> GetLatestByExecutionIdAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var row = await ctx.HumanInteractions.AsNoTracking()
            .Where(r => r.ExecutionId == executionId)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : Map(row);
    }

    public async Task ExpireByExecutionIdAsync(string executionId, CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        await ctx.HumanInteractions
            .Where(r => r.ExecutionId == executionId && r.Status == nameof(HumanInteractionStatus.Pending))
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, nameof(HumanInteractionStatus.Expired))
                .SetProperty(r => r.ResolvedAt, DateTime.UtcNow),
                ct);
    }

    public async Task<bool> TryResolveAsync(
        string interactionId,
        HumanInteractionStatus newStatus,
        string resolution,
        DateTime resolvedAt,
        string resolvedBy,
        CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        var pendingStr = nameof(HumanInteractionStatus.Pending);
        var newStatusStr = newStatus.ToString();
        // ExecuteUpdateAsync gera UPDATE ... WHERE ... em uma única ida ao banco,
        // garantindo atomicidade do CAS. rowsAffected=1 → este caller venceu; 0 → já foi resolvido.
        var rowsAffected = await ctx.HumanInteractions
            .Where(r => r.InteractionId == interactionId
                     && r.Status == pendingStr)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Status, newStatusStr)
                .SetProperty(r => r.Resolution, resolution)
                .SetProperty(r => r.ResolvedAt, (DateTime?)resolvedAt)
                .SetProperty(r => r.ResolvedBy, (string?)resolvedBy),
                ct);
        return rowsAffected > 0;
    }

    public async Task ExpireOrphanedAsync(CancellationToken ct = default)
    {
        await using var ctx = await factory.CreateDbContextAsync(ct);
        // SQL raw para evitar dependência da classe internal WorkflowExecutionRow.
        // Expira em lote todos os HITLs Pending cujas execuções já estão em estado terminal.
        await ctx.Database.ExecuteSqlRawAsync(@"
UPDATE human_interactions
SET    ""Status""     = 'Expired',
       ""ResolvedAt"" = NOW()
WHERE  ""Status"" = 'Pending'
  AND  ""ExecutionId"" IN (
       SELECT ""ExecutionId""
       FROM   workflow_executions
       WHERE  ""Status"" IN ('Failed', 'Cancelled', 'Completed')
  )", ct);
    }

    private static HumanInteractionRequest Map(HumanInteractionRow row) => new()
    {
        InteractionId = row.InteractionId,
        ExecutionId = row.ExecutionId,
        WorkflowId = row.WorkflowId,
        Prompt = row.Prompt,
        Context = row.Context,
        InteractionType = Enum.TryParse<InteractionType>(row.InteractionType, out var it)
            ? it
            : InteractionType.Approval,
        Options = !string.IsNullOrEmpty(row.Options)
            ? JsonSerializer.Deserialize<List<string>>(row.Options)
            : null,
        Status = Enum.Parse<HumanInteractionStatus>(row.Status),
        Resolution = row.Resolution,
        CreatedAt = row.CreatedAt,
        ResolvedAt = row.ResolvedAt,
        ResolvedBy = row.ResolvedBy
    };
}
