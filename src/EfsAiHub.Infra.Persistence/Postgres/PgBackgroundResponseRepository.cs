using System.Text.Json;
using EfsAiHub.Core.Agents.Responses;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Persistência de <see cref="BackgroundResponseJob"/>. Segue o mesmo
/// padrão factory-per-op dos demais repositórios Pg.
/// </summary>
public sealed class PgBackgroundResponseRepository : IBackgroundResponseRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgBackgroundResponseRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<BackgroundResponseJob> InsertAsync(BackgroundResponseJob job, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        ctx.BackgroundResponseJobs.Add(ToRow(job));
        await ctx.SaveChangesAsync(ct);
        return job;
    }

    public async Task<BackgroundResponseJob?> GetAsync(string jobId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.BackgroundResponseJobs.FindAsync([jobId], ct);
        return row is null ? null : FromRow(row);
    }

    public async Task<BackgroundResponseJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.BackgroundResponseJobs
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct);
        return row is null ? null : FromRow(row);
    }

    public async Task UpdateAsync(BackgroundResponseJob job, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.BackgroundResponseJobs.FindAsync([job.JobId], ct)
            ?? throw new InvalidOperationException($"Job '{job.JobId}' não encontrado.");
        row.Status = job.Status.ToString();
        row.Output = job.Output;
        row.LastError = job.LastError;
        row.Attempt = job.Attempt;
        row.StartedAt = job.StartedAt;
        row.CompletedAt = job.CompletedAt;
        row.CallbackTarget = job.CallbackTarget is null ? null : JsonSerializer.Serialize(job.CallbackTarget, JsonDefaults.Domain);
        await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<BackgroundResponseJob>> ListPendingAsync(int limit, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var rows = await ctx.BackgroundResponseJobs
            .Where(r => r.Status == "Queued" || r.Status == "Running")
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(FromRow).ToList();
    }

    private static BackgroundResponseJobRow ToRow(BackgroundResponseJob j) => new()
    {
        JobId = j.JobId,
        AgentId = j.AgentId,
        AgentVersionId = j.AgentVersionId,
        SessionId = j.SessionId,
        Input = j.Input,
        Status = j.Status.ToString(),
        Output = j.Output,
        LastError = j.LastError,
        Attempt = j.Attempt,
        CallbackTarget = j.CallbackTarget is null ? null : JsonSerializer.Serialize(j.CallbackTarget, JsonDefaults.Domain),
        IdempotencyKey = j.IdempotencyKey,
        CreatedAt = j.CreatedAt,
        StartedAt = j.StartedAt,
        CompletedAt = j.CompletedAt
    };

    private static BackgroundResponseJob FromRow(BackgroundResponseJobRow r) => new()
    {
        JobId = r.JobId,
        AgentId = r.AgentId,
        AgentVersionId = r.AgentVersionId,
        SessionId = r.SessionId,
        Input = r.Input,
        Status = Enum.TryParse<BackgroundResponseStatus>(r.Status, out var s) ? s : BackgroundResponseStatus.Queued,
        Output = r.Output,
        LastError = r.LastError,
        Attempt = r.Attempt,
        CallbackTarget = string.IsNullOrEmpty(r.CallbackTarget)
            ? null
            : JsonSerializer.Deserialize<ResponseCallbackTarget>(r.CallbackTarget, JsonDefaults.Domain),
        IdempotencyKey = r.IdempotencyKey,
        CreatedAt = r.CreatedAt,
        StartedAt = r.StartedAt,
        CompletedAt = r.CompletedAt
    };
}
