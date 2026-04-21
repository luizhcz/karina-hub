using System.Text.Json;
using EfsAiHub.Core.Orchestration.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgWorkflowExecutionRepository : IWorkflowExecutionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly ILogger<PgWorkflowExecutionRepository> _logger;

    public PgWorkflowExecutionRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        ILogger<PgWorkflowExecutionRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    private static WorkflowExecution Deserialize(WorkflowExecutionRow row)
        => JsonSerializer.Deserialize<WorkflowExecution>(row.Data, JsonDefaults.Domain)!;

    public async Task<WorkflowExecution?> GetByIdAsync(
        string executionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowExecutions.FindAsync([executionId], ct);
        return row is null ? null : Deserialize(row);
    }

    public async Task<IReadOnlyList<WorkflowExecution>> GetByIdsAsync(
        IEnumerable<string> executionIds, CancellationToken ct = default)
    {
        var idList = executionIds.ToList();
        if (idList.Count == 0) return Array.Empty<WorkflowExecution>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.WorkflowExecutions
            .Where(r => idList.Contains(r.ExecutionId))
            .ToListAsync(ct);

        return rows.Select(Deserialize).ToList();
    }

    public async Task<IReadOnlyList<WorkflowExecution>> GetByWorkflowIdAsync(
        string workflowId, int page = 1, int pageSize = 20, string? status = null, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.WorkflowExecutions.Where(r => r.WorkflowId == workflowId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);
        var rows = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(Deserialize).ToList();
    }

    public async Task<WorkflowExecution> CreateAsync(
        WorkflowExecution execution, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.WorkflowExecutions.Add(new WorkflowExecutionRow
        {
            ExecutionId = execution.ExecutionId,
            WorkflowId = execution.WorkflowId,
            ProjectId = execution.ProjectId,
            Status = execution.Status.ToString(),
            Data = JsonSerializer.Serialize(execution, JsonDefaults.Domain),
            StartedAt = execution.StartedAt,
            CompletedAt = execution.CompletedAt
        });
        await ctx.SaveChangesAsync(ct);
        return execution;
    }

    public async Task<WorkflowExecution> UpdateAsync(
        WorkflowExecution execution, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.WorkflowExecutions.FindAsync([execution.ExecutionId], ct);
        if (row is null)
            return await CreateAsync(execution, ct);

        row.Status = execution.Status.ToString();
        row.Data = JsonSerializer.Serialize(execution, JsonDefaults.Domain);
        row.CompletedAt = execution.CompletedAt;
        await ctx.SaveChangesAsync(ct);
        return execution;
    }

    public async Task<IReadOnlyList<WorkflowExecution>> GetActiveExecutionsAsync(
        int maxLimit = 1000, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var activeStatuses = new[]
        {
            nameof(WorkflowStatus.Pending),
            nameof(WorkflowStatus.Running),
            nameof(WorkflowStatus.Paused)
        };
        var rows = await ctx.WorkflowExecutions
            .Where(r => activeStatuses.Contains(r.Status))
            .OrderBy(r => r.StartedAt)
            .Take(maxLimit)
            .ToListAsync(ct);

        if (rows.Count == maxLimit)
            _logger.LogWarning("[ActiveExec] Limite de {MaxLimit} atingido — pode haver mais execuções ativas não carregadas.", maxLimit);

        return rows.Select(Deserialize).ToList();
    }

    public async Task<IReadOnlyList<WorkflowExecution>> GetPausedExecutionsPagedAsync(
        int offset, int pageSize, CancellationToken ct = default)
    {
        if (offset < 0) offset = 0;
        if (pageSize <= 0) pageSize = 100;
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var pausedName = nameof(WorkflowStatus.Paused);
        var rows = await ctx.WorkflowExecutions
            .Where(r => r.Status == pausedName)
            .OrderBy(r => r.StartedAt)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);
        return rows.Select(Deserialize).ToList();
    }

    public async Task<int> CountPausedAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var pausedName = nameof(WorkflowStatus.Paused);
        return await ctx.WorkflowExecutions.CountAsync(r => r.Status == pausedName, ct);
    }

    public async Task<int> CountRunningAsync(string workflowId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.WorkflowExecutions
            .CountAsync(r => r.WorkflowId == workflowId && r.Status == nameof(WorkflowStatus.Running), ct);
    }

    public async Task<IReadOnlyList<WorkflowExecution>> GetAllAsync(
        string? workflowId = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var rows = await BuildExecutionQuery(ctx, workflowId, status, from, to)
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(Deserialize).ToList();
    }

    public async Task<int> CountAsync(
        string? workflowId = null,
        string? status = null,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await BuildExecutionQuery(ctx, workflowId, status, from, to).CountAsync(ct);
    }

    /// <summary>
    /// Aplica os filtros opcionais (workflowId, status, intervalo de datas) sobre
    /// a tabela de execuções. Compartilhado por <see cref="GetAllAsync"/> e <see cref="CountAsync"/>.
    /// </summary>
    private static IQueryable<WorkflowExecutionRow> BuildExecutionQuery(
        AgentFwDbContext ctx,
        string? workflowId,
        string? status,
        DateTime? from,
        DateTime? to)
    {
        var query = ctx.WorkflowExecutions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(workflowId))
            query = query.Where(r => r.WorkflowId == workflowId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);
        if (from.HasValue)
            query = query.Where(r => r.StartedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.StartedAt <= to.Value);
        return query;
    }
}
