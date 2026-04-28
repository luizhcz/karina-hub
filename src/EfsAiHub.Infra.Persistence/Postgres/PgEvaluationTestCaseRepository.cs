using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public sealed class PgEvaluationTestCaseRepository : IEvaluationTestCaseRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluationTestCaseRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluationTestCase?> GetByIdAsync(string caseId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestCases.FindAsync([caseId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluationTestCase>> ListByVersionAsync(
        string testSetVersionId,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.EvaluationTestCases
            .Where(r => r.TestSetVersionId == testSetVersionId)
            .OrderBy(r => r.Index)
            .AsQueryable();
        if (skip.HasValue) query = query.Skip(skip.Value);
        if (take.HasValue) query = query.Take(take.Value);
        var rows = await query.ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<EvaluationTestCase>> ListByVersionAndTagsAsync(
        string testSetVersionId,
        IReadOnlyList<string> tags,
        CancellationToken ct = default)
    {
        if (tags.Count == 0)
            return await ListByVersionAsync(testSetVersionId, ct: ct);

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // Array overlap (&&) — EF não traduz; raw SQL aproveita índice GIN.
        var rows = await ctx.EvaluationTestCases
            .FromSqlInterpolated($@"
SELECT * FROM aihub.evaluation_test_cases
 WHERE ""TestSetVersionId"" = {testSetVersionId}
   AND ""Tags"" && {tags.ToArray()}
 ORDER BY ""Index""")
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> CountByVersionAsync(string testSetVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        return await ctx.EvaluationTestCases.CountAsync(r => r.TestSetVersionId == testSetVersionId, ct);
    }

    private static EvaluationTestCase ToDomain(EvaluationTestCaseRow row)
    {
        JsonDocument? expectedToolCalls = null;
        if (!string.IsNullOrEmpty(row.ExpectedToolCalls))
            expectedToolCalls = JsonDocument.Parse(row.ExpectedToolCalls);

        return new EvaluationTestCase(
            CaseId: row.CaseId,
            TestSetVersionId: row.TestSetVersionId,
            Index: row.Index,
            Input: row.Input,
            ExpectedOutput: row.ExpectedOutput,
            ExpectedToolCalls: expectedToolCalls,
            Tags: row.Tags,
            Weight: row.Weight,
            CreatedAt: row.CreatedAt);
    }
}
