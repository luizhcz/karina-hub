using System.Text.Json;
using EfsAiHub.Core.Orchestration.Executors;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgNodeExecutionRepository : INodeExecutionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgNodeExecutionRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    private const int MaxOutputLength = 2000;

    public async Task SetNodeAsync(NodeExecutionRecord record, CancellationToken ct = default)
    {
        // Truncar output antes de persistir (output completo acessível via chat_messages)
        if (record.Output?.Length > MaxOutputLength)
        {
            record.Output = record.Output[..MaxOutputLength];
            record.OutputTruncated = true;
        }

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var data = JsonSerializer.Serialize(record, JsonDefaults.Domain);

        var existing = await ctx.NodeExecutions
            .FirstOrDefaultAsync(
                n => n.ExecutionId == record.ExecutionId && n.NodeId == record.NodeId, ct);

        if (existing is null)
            ctx.NodeExecutions.Add(new NodeExecutionRow
            {
                ExecutionId = record.ExecutionId,
                NodeId = record.NodeId,
                Data = data,
                // Propaga ProjectId do ExecutionContext ambiente (AsyncLocal).
                // Null se execução tiver sido triggered sem projectId no metadata —
                // HasQueryFilter tolera mas é débito de tenancy ainda aberto.
                ProjectId = DelegateExecutor.Current.Value?.ProjectId,
            });
        else
            existing.Data = data;

        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NodeExecutionRecord>> GetAllAsync(
        string executionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.NodeExecutions
            .Where(n => n.ExecutionId == executionId)
            .ToListAsync(ct);

        return rows
            .Select(r => JsonSerializer.Deserialize<NodeExecutionRecord>(r.Data, JsonDefaults.Domain)!)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<NodeExecutionRecord>>> GetAllByExecutionIdsAsync(
        IEnumerable<string> executionIds, CancellationToken ct = default)
    {
        var idList = executionIds.ToList();
        if (idList.Count == 0)
            return new Dictionary<string, IReadOnlyList<NodeExecutionRecord>>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.NodeExecutions
            .Where(n => idList.Contains(n.ExecutionId))
            .ToListAsync(ct);

        return rows
            .Select(r => JsonSerializer.Deserialize<NodeExecutionRecord>(r.Data, JsonDefaults.Domain)!)
            .GroupBy(n => n.ExecutionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<NodeExecutionRecord>)g.ToList());
    }

    public async Task<NodeExecutionRecord?> GetNodeAsync(
        string executionId, string nodeId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.NodeExecutions
            .FirstOrDefaultAsync(
                n => n.ExecutionId == executionId && n.NodeId == nodeId, ct);

        return row is null
            ? null
            : JsonSerializer.Deserialize<NodeExecutionRecord>(row.Data, JsonDefaults.Domain);
    }
}
