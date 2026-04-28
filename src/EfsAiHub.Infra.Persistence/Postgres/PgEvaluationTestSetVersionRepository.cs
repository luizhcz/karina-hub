using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append-only de versions de TestSet. Idempotência por ContentHash: row
/// existente não-Deprecated → no-op. Race em publishes concorrentes captura
/// 23505 e retorna o vencedor.
/// </summary>
public sealed class PgEvaluationTestSetVersionRepository : IEvaluationTestSetVersionRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluationTestSetVersionRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluationTestSetVersion?> GetByIdAsync(string testSetVersionId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestSetVersions.FindAsync([testSetVersionId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluationTestSetVersion>> ListByTestSetAsync(string testSetId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.EvaluationTestSetVersions
            .Where(r => r.TestSetId == testSetId)
            .OrderByDescending(r => r.Revision)
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> GetNextRevisionAsync(string testSetId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var max = await ctx.EvaluationTestSetVersions
            .Where(r => r.TestSetId == testSetId)
            .Select(r => (int?)r.Revision)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<EvaluationTestSetVersion> AppendAsync(
        EvaluationTestSetVersion version,
        IReadOnlyList<EvaluationTestCase> cases,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var dup = await ctx.EvaluationTestSetVersions
            .Where(r => r.TestSetId == version.TestSetId
                     && r.ContentHash == version.ContentHash
                     && r.Status != "Deprecated")
            .FirstOrDefaultAsync(ct);
        if (dup is not null) return ToDomain(dup);

        ctx.EvaluationTestSetVersions.Add(new EvaluationTestSetVersionRow
        {
            TestSetVersionId = version.TestSetVersionId,
            TestSetId = version.TestSetId,
            Revision = version.Revision,
            Status = version.Status.ToString(),
            ContentHash = version.ContentHash,
            CreatedAt = version.CreatedAt,
            CreatedBy = version.CreatedBy,
            ChangeReason = version.ChangeReason
        });

        // Tx atômica: version antes de cases (FK), commit conjunto. Pod morrendo
        // entre os 2 saves não deixa version fantasma sem cases.
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        try
        {
            await ctx.SaveChangesAsync(ct);

            foreach (var c in cases)
            {
                ctx.EvaluationTestCases.Add(new EvaluationTestCaseRow
                {
                    CaseId = c.CaseId,
                    TestSetVersionId = version.TestSetVersionId,
                    Index = c.Index,
                    Input = c.Input,
                    ExpectedOutput = c.ExpectedOutput,
                    ExpectedToolCalls = c.ExpectedToolCalls?.RootElement.GetRawText(),
                    Tags = c.Tags.ToArray(),
                    Weight = c.Weight,
                    CreatedAt = c.CreatedAt
                });
            }
            await ctx.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return version;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
        {
            // Race: outro caller publicou o mesmo ContentHash entre check e insert.
            await using var ctx2 = await _factory.CreateDbContextAsync(ct);
            var winner = await ctx2.EvaluationTestSetVersions
                .Where(r => r.TestSetId == version.TestSetId
                         && r.ContentHash == version.ContentHash
                         && r.Status != "Deprecated")
                .FirstAsync(ct);
            return ToDomain(winner);
        }
    }

    public async Task SetStatusAsync(string testSetVersionId, TestSetVersionStatus status, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationTestSetVersions.FindAsync([testSetVersionId], ct)
            ?? throw new InvalidOperationException($"TestSetVersion '{testSetVersionId}' não encontrada.");
        row.Status = status.ToString();
        await ctx.SaveChangesAsync(ct);
    }

    private static EvaluationTestSetVersion ToDomain(EvaluationTestSetVersionRow row)
    {
        var status = Enum.TryParse<TestSetVersionStatus>(row.Status, out var s) ? s : TestSetVersionStatus.Draft;
        return new EvaluationTestSetVersion(
            TestSetVersionId: row.TestSetVersionId,
            TestSetId: row.TestSetId,
            Revision: row.Revision,
            Status: status,
            ContentHash: row.ContentHash,
            CreatedAt: row.CreatedAt,
            CreatedBy: row.CreatedBy,
            ChangeReason: row.ChangeReason);
    }
}
