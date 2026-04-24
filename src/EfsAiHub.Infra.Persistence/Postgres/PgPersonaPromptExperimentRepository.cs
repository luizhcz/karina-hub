using EfsAiHub.Core.Abstractions.Identity.Persona;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgPersonaPromptExperimentRepository : IPersonaPromptExperimentRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgPersonaPromptExperimentRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task<PersonaPromptExperiment?> GetActiveAsync(
        string projectId, string scope, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptExperiments.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.ProjectId == projectId
                  && e.Scope == scope
                  && e.EndedAt == null,
                ct);
        return row is null ? null : Map(row);
    }

    public async Task<PersonaPromptExperiment?> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptExperiments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);
        return row is null ? null : Map(row);
    }

    public async Task<IReadOnlyList<PersonaPromptExperiment>> GetByProjectAsync(
        string projectId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.PersonaPromptExperiments.AsNoTracking()
            .Where(e => e.ProjectId == projectId)
            .OrderByDescending(e => e.StartedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<PersonaPromptExperiment> CreateAsync(
        PersonaPromptExperiment experiment, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = new PersonaPromptExperimentRow
        {
            ProjectId = experiment.ProjectId,
            Scope = experiment.Scope,
            Name = experiment.Name,
            VariantAVersionId = experiment.VariantAVersionId,
            VariantBVersionId = experiment.VariantBVersionId,
            TrafficSplitB = experiment.TrafficSplitB,
            Metric = experiment.Metric,
            StartedAt = experiment.StartedAt == default ? DateTime.UtcNow : experiment.StartedAt,
            EndedAt = experiment.EndedAt,
            CreatedBy = experiment.CreatedBy,
        };
        ctx.PersonaPromptExperiments.Add(row);
        await ctx.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<bool> EndAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptExperiments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return false;
        if (row.EndedAt is not null) return true; // idempotente
        row.EndedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.PersonaPromptExperiments.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (row is null) return false;
        ctx.PersonaPromptExperiments.Remove(row);
        await ctx.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<ExperimentVariantResult>> GetResultsAsync(
        int experimentId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // Aggregate via raw SQL — EF IgnoreQueryFilters + GroupBy complica por
        // causa do filter em LlmTokenUsageRow. Raw SQL é legível e não
        // participa do filter (admin-only). Variant é CHAR(1) no schema.
        var rows = await ctx.Database
            .SqlQueryRaw<VariantAggRaw>(
                """
                SELECT
                    "ExperimentVariant"                AS "Variant",
                    COUNT(*)::int                       AS "SampleCount",
                    COALESCE(SUM("TotalTokens"), 0)::bigint  AS "TotalTokens",
                    COALESCE(SUM("CachedTokens"), 0)::bigint AS "CachedTokens",
                    COALESCE(AVG("TotalTokens"), 0)     AS "AvgTotalTokens",
                    COALESCE(AVG("DurationMs"), 0)      AS "AvgDurationMs"
                FROM llm_token_usage
                WHERE "ExperimentId" = {0}
                GROUP BY "ExperimentVariant"
                ORDER BY "ExperimentVariant"
                """,
                experimentId)
            .ToListAsync(ct);

        return rows
            .Where(r => !string.IsNullOrEmpty(r.Variant))
            .Select(r => new ExperimentVariantResult
            {
                Variant = r.Variant![0],
                SampleCount = r.SampleCount,
                TotalTokens = r.TotalTokens,
                CachedTokens = r.CachedTokens,
                AvgTotalTokens = r.AvgTotalTokens,
                AvgDurationMs = r.AvgDurationMs,
            })
            .ToList();
    }

    private static PersonaPromptExperiment Map(PersonaPromptExperimentRow row) => new()
    {
        Id = row.Id,
        ProjectId = row.ProjectId,
        Scope = row.Scope,
        Name = row.Name,
        VariantAVersionId = row.VariantAVersionId,
        VariantBVersionId = row.VariantBVersionId,
        TrafficSplitB = row.TrafficSplitB,
        Metric = row.Metric,
        StartedAt = row.StartedAt,
        EndedAt = row.EndedAt,
        CreatedBy = row.CreatedBy,
    };

    private sealed class VariantAggRaw
    {
        public string? Variant { get; set; }
        public int SampleCount { get; set; }
        public long TotalTokens { get; set; }
        public long CachedTokens { get; set; }
        public double AvgTotalTokens { get; set; }
        public double AvgDurationMs { get; set; }
    }
}
