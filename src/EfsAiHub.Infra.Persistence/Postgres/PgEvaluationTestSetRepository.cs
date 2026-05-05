using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>Header CRUD de TestSets; versions append-only em <see cref="PgEvaluationTestSetVersionRepository"/>.</summary>
public sealed class PgEvaluationTestSetRepository : IEvaluationTestSetRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluationTestSetRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluationTestSet?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestSets.FindAsync([id], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluationTestSet>> ListByProjectAsync(
        string projectId,
        bool includeGlobal = true,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.EvaluationTestSets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.ProjectId == projectId || (includeGlobal && r.Visibility == "global"));
        var rows = await query
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyDictionary<string, TestSetVersionStats>> GetStatsForVersionsAsync(
        IEnumerable<string> testSetVersionIds,
        CancellationToken ct = default)
    {
        var ids = testSetVersionIds?.Distinct().ToArray() ?? Array.Empty<string>();
        if (ids.Length == 0) return new Dictionary<string, TestSetVersionStats>();

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        // 2 queries simples: revision por version + count de cases por version.
        // Mais barato que single JOIN agregado (cada uma usa o índice da PK/FK
        // direto). Merge em memória — IDs in() é small (limit prático: 50-100
        // test sets por projeto).
        var revisions = await ctx.EvaluationTestSetVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(v => ids.Contains(v.TestSetVersionId))
            .Select(v => new { v.TestSetVersionId, v.Revision })
            .ToListAsync(ct);

        var counts = await ctx.EvaluationTestCases
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => ids.Contains(c.TestSetVersionId))
            .GroupBy(c => c.TestSetVersionId)
            .Select(g => new { TestSetVersionId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var countMap = counts.ToDictionary(x => x.TestSetVersionId, x => x.Count);
        return revisions.ToDictionary(
            r => r.TestSetVersionId,
            r => new TestSetVersionStats(
                CaseCount: countMap.TryGetValue(r.TestSetVersionId, out var c) ? c : 0,
                Revision: r.Revision));
    }

    public async Task<EvaluationTestSet> UpsertAsync(EvaluationTestSet testSet, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var existing = await ctx.EvaluationTestSets.FindAsync([testSet.Id], ct);
        if (existing is null)
        {
            ctx.EvaluationTestSets.Add(new EvaluationTestSetRow
            {
                Id = testSet.Id,
                ProjectId = testSet.ProjectId,
                Visibility = testSet.Visibility.ToString().ToLowerInvariant(),
                Name = testSet.Name,
                Description = testSet.Description,
                CurrentVersionId = testSet.CurrentVersionId,
                CreatedAt = testSet.CreatedAt,
                UpdatedAt = testSet.UpdatedAt,
                CreatedBy = testSet.CreatedBy
            });
        }
        else
        {
            existing.ProjectId = testSet.ProjectId;
            existing.Visibility = testSet.Visibility.ToString().ToLowerInvariant();
            existing.Name = testSet.Name;
            existing.Description = testSet.Description;
            existing.CurrentVersionId = testSet.CurrentVersionId;
            existing.UpdatedAt = testSet.UpdatedAt;
        }
        await ctx.SaveChangesAsync(ct);
        return testSet;
    }

    public async Task SetCurrentVersionAsync(string testSetId, string testSetVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestSets.FindAsync([testSetId], ct)
            ?? throw new InvalidOperationException($"TestSet '{testSetId}' não encontrado.");
        row.CurrentVersionId = testSetVersionId;
        row.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestSets.FindAsync([id], ct);
        if (row is null) return;
        ctx.EvaluationTestSets.Remove(row);
        await ctx.SaveChangesAsync(ct);
    }

    private static EvaluationTestSet ToDomain(EvaluationTestSetRow row)
    {
        var visibility = string.Equals(row.Visibility, "global", StringComparison.OrdinalIgnoreCase)
            ? TestSetVisibility.Global
            : TestSetVisibility.Project;

        return new EvaluationTestSet(
            Id: row.Id,
            ProjectId: row.ProjectId,
            Name: row.Name,
            Description: row.Description,
            Visibility: visibility,
            CurrentVersionId: row.CurrentVersionId,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt,
            CreatedBy: row.CreatedBy);
    }
}
