using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgToolInvocationRepository : IToolInvocationRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgToolInvocationRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task AppendAsync(ToolInvocation invocation, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.ToolInvocations.Add(new ToolInvocationRow
        {
            ExecutionId = invocation.ExecutionId,
            AgentId = invocation.AgentId,
            ToolName = invocation.ToolName,
            Arguments = invocation.Arguments,
            Result = invocation.Result?.Length > 500 ? invocation.Result[..500] : invocation.Result,
            DurationMs = invocation.DurationMs,
            Success = invocation.Success,
            ErrorMessage = invocation.ErrorMessage,
            CreatedAt = invocation.CreatedAt
        });
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ToolInvocation>> GetByExecutionAsync(
        string executionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.ToolInvocations
            .Where(r => r.ExecutionId == executionId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return rows.Select(MapRow).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ToolInvocation>>> GetByExecutionIdsAsync(
        IEnumerable<string> executionIds, CancellationToken ct = default)
    {
        var idList = executionIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<string, IReadOnlyList<ToolInvocation>>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.ToolInvocations
            .Where(r => idList.Contains(r.ExecutionId))
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);

        return rows
            .Select(MapRow)
            .GroupBy(t => t.ExecutionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<ToolInvocation>)g.ToList());
    }

    private static ToolInvocation MapRow(ToolInvocationRow r) => new()
    {
        Id = r.Id,
        ExecutionId = r.ExecutionId,
        AgentId = r.AgentId,
        ToolName = r.ToolName,
        Arguments = r.Arguments,
        Result = r.Result,
        DurationMs = r.DurationMs,
        Success = r.Success,
        ErrorMessage = r.ErrorMessage,
        CreatedAt = r.CreatedAt
    };
}
