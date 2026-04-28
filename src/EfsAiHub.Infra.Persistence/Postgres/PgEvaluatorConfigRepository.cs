using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgEvaluatorConfigRepository : IEvaluatorConfigRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluatorConfigRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluatorConfig?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigs.FindAsync([id], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<EvaluatorConfig?> GetByAgentDefinitionAsync(string agentDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // 1 EvaluatorConfig ativo por agent (último por UpdatedAt); múltiplos
        // co-existem como variantes geridas pela UI.
        var row = await ctx.EvaluatorConfigs
            .Where(r => r.AgentDefinitionId == agentDefinitionId)
            .OrderByDescending(r => r.UpdatedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluatorConfig>> ListByAgentDefinitionAsync(string agentDefinitionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.EvaluatorConfigs
            .Where(r => r.AgentDefinitionId == agentDefinitionId)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<EvaluatorConfig> UpsertAsync(EvaluatorConfig config, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var existing = await ctx.EvaluatorConfigs.FindAsync([config.Id], ct);
        if (existing is null)
        {
            ctx.EvaluatorConfigs.Add(new EvaluatorConfigRow
            {
                Id = config.Id,
                AgentDefinitionId = config.AgentDefinitionId,
                Name = config.Name,
                CurrentVersionId = config.CurrentVersionId,
                CreatedAt = config.CreatedAt,
                UpdatedAt = config.UpdatedAt,
                CreatedBy = config.CreatedBy
            });
        }
        else
        {
            existing.Name = config.Name;
            existing.CurrentVersionId = config.CurrentVersionId;
            existing.UpdatedAt = config.UpdatedAt;
        }
        await ctx.SaveChangesAsync(ct);
        return config;
    }

    public async Task SetCurrentVersionAsync(string configId, string evaluatorConfigVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigs.FindAsync([configId], ct)
            ?? throw new InvalidOperationException($"EvaluatorConfig '{configId}' não encontrado.");
        row.CurrentVersionId = evaluatorConfigVersionId;
        row.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluatorConfigs.FindAsync([id], ct);
        if (row is null) return;
        ctx.EvaluatorConfigs.Remove(row);
        await ctx.SaveChangesAsync(ct);
    }

    private static EvaluatorConfig ToDomain(EvaluatorConfigRow row) => new(
        Id: row.Id,
        AgentDefinitionId: row.AgentDefinitionId,
        Name: row.Name,
        CurrentVersionId: row.CurrentVersionId,
        CreatedAt: row.CreatedAt,
        UpdatedAt: row.UpdatedAt,
        CreatedBy: row.CreatedBy);
}
