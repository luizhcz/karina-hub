using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Header de eval runs. Autotrigger idempotente via INSERT ON CONFLICT
/// (re-publish da mesma version não cria run duplicada). Dequeue atômico
/// com FOR UPDATE SKIP LOCKED — N pods concorrentes nunca pegam a mesma
/// row, transição Pending→Running embutida. Cancel usa CAS via
/// <see cref="TryTransitionStatusAsync"/>.
/// </summary>
public sealed class PgEvaluationRunRepository : IEvaluationRunRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluationRunRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<EvaluationRun?> GetByIdAsync(string runId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationRuns.FindAsync([runId], ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<IReadOnlyList<EvaluationRun>> ListByAgentDefinitionAsync(
        string agentDefinitionId,
        int? skip = null,
        int? take = null,
        EvaluationTriggerSource? triggerSourceFilter = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.EvaluationRuns
            .Where(r => r.AgentDefinitionId == agentDefinitionId);

        if (triggerSourceFilter.HasValue)
        {
            var ts = triggerSourceFilter.Value.ToString();
            query = query.Where(r => r.TriggerSource == ts);
        }

        query = query.OrderByDescending(r => r.CreatedAt);
        if (skip.HasValue) query = query.Skip(skip.Value);
        if (take.HasValue) query = query.Take(take.Value);

        var rows = await query.ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<EvaluationRun?> EnqueueAsync(EvaluationRun run, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // Postgres não aceita ON CONFLICT ON CONSTRAINT contra unique index
        // PARCIAL (partial unique indexes não viram constraints). Solução:
        // ON CONFLICT ((cols)) WHERE (predicate) match o índice por shape.
        var triggerContextJson = run.TriggerContext?.RootElement.GetRawText();
        var rowsAffected = await ctx.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO aihub.evaluation_runs (
    ""RunId"", ""ProjectId"", ""AgentDefinitionId"", ""AgentVersionId"",
    ""TestSetVersionId"", ""EvaluatorConfigVersionId"", ""BaselineRunId"",
    ""Status"", ""Priority"", ""TriggeredBy"", ""TriggerSource"", ""TriggerContext"",
    ""ExecutionId"", ""CasesTotal"",
    ""StartedAt"", ""CompletedAt"", ""LastHeartbeatAt"", ""LastError"", ""CreatedAt"")
VALUES (
    {run.RunId}, {run.ProjectId}, {run.AgentDefinitionId}, {run.AgentVersionId},
    {run.TestSetVersionId}, {run.EvaluatorConfigVersionId}, {run.BaselineRunId},
    {run.Status.ToString()}, {run.Priority}, {run.TriggeredBy}, {run.TriggerSource.ToString()},
    {triggerContextJson}::jsonb,
    {run.ExecutionId}, {run.CasesTotal},
    {run.StartedAt}, {run.CompletedAt}, {run.LastHeartbeatAt}, {run.LastError}, {run.CreatedAt})
ON CONFLICT (""AgentVersionId"") WHERE ""TriggerSource"" = 'AgentVersionPublished' AND ""Status"" IN ('Pending','Running','Completed') DO NOTHING
", ct);

        if (rowsAffected == 1)
        {
            return run;
        }

        // Conflito: busca a run que vetou o INSERT. Índice parcial cobre Status
        // IN (Pending, Running, Completed) — existing row está em um desses 3.
        await using var ctx2 = await _factory.CreateDbContextAsync(ct);
        var existing = await ctx2.EvaluationRuns
            .Where(r => r.AgentVersionId == run.AgentVersionId
                     && r.TriggerSource == "AgentVersionPublished"
                     && (r.Status == "Pending" || r.Status == "Running" || r.Status == "Completed"))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return existing is null ? null : ToDomain(existing);
    }

    public async Task<bool> TryTransitionStatusAsync(
        string runId,
        EvaluationRunStatus from,
        EvaluationRunStatus to,
        string? lastError = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var fromStr = from.ToString();
        var toStr = to.ToString();
        var now = DateTime.UtcNow;

        // CAS atômico — UPDATE ... WHERE status = from evita last-writer-wins
        // entre runner finalizando vs operador cancelando.
        var rows = await ctx.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE aihub.evaluation_runs
   SET ""Status"" = {toStr},
       ""LastError"" = COALESCE({lastError}, ""LastError""),
       ""StartedAt""   = CASE WHEN {toStr} = 'Running'   AND ""StartedAt""   IS NULL THEN {now}::timestamptz ELSE ""StartedAt"" END,
       ""CompletedAt"" = CASE WHEN {toStr} IN ('Completed','Failed','Cancelled') THEN {now}::timestamptz ELSE ""CompletedAt"" END
 WHERE ""RunId"" = {runId}
   AND ""Status"" = {fromStr}
", ct);
        return rows > 0;
    }

    public async Task TouchHeartbeatAsync(string runId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE aihub.evaluation_runs
   SET ""LastHeartbeatAt"" = {now}::timestamptz
 WHERE ""RunId"" = {runId}
   AND ""Status"" = 'Running'
", ct);
    }

    public async Task<EvaluationRun?> FindBaselineAsync(
        string agentDefinitionId,
        string testSetVersionId,
        string evaluatorConfigVersionId,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationRuns
            .Where(r => r.AgentDefinitionId == agentDefinitionId
                     && r.TestSetVersionId == testSetVersionId
                     && r.EvaluatorConfigVersionId == evaluatorConfigVersionId
                     && r.Status == "Completed")
            .OrderByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : ToDomain(row);
    }

    public async Task<EvaluationRun?> DequeuePendingAsync(CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        // FOR UPDATE SKIP LOCKED no sub-SELECT: dequeue + transição
        // Pending→Running atômicos. Sem isso, 2 pods retornam a mesma row e
        // 1 perde o CAS depois. EF Core 10 não compõe UPDATE...RETURNING em
        // FromSqlInterpolated, daí ADO.NET direto.
        var now = DateTime.UtcNow;
        var conn = ctx.Database.GetDbConnection();
        await ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE aihub.evaluation_runs
   SET ""Status"" = 'Running',
       ""StartedAt"" = COALESCE(""StartedAt"", @now),
       ""LastHeartbeatAt"" = @now
 WHERE ""RunId"" = (
   SELECT ""RunId"" FROM aihub.evaluation_runs
    WHERE ""Status"" = 'Pending'
    ORDER BY ""Priority"" ASC, ""CreatedAt"" ASC
    LIMIT 1
    FOR UPDATE SKIP LOCKED
 )
 RETURNING *";
            var p = cmd.CreateParameter();
            p.ParameterName = "now";
            p.Value = now;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return ReadEvaluationRun(reader);
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static EvaluationRun ReadEvaluationRun(System.Data.Common.DbDataReader r)
    {
        string? GetNullableString(int idx) => r.IsDBNull(idx) ? null : r.GetString(idx);
        DateTime? GetNullableDate(int idx) => r.IsDBNull(idx) ? null : r.GetDateTime(idx);

        string? triggerContextRaw = null;
        var triggerContextOrdinal = r.GetOrdinal("TriggerContext");
        if (!r.IsDBNull(triggerContextOrdinal))
            triggerContextRaw = r.GetString(triggerContextOrdinal);

        var status = Enum.TryParse<EvaluationRunStatus>(r.GetString(r.GetOrdinal("Status")), out var s)
            ? s : EvaluationRunStatus.Pending;
        var trigger = Enum.TryParse<EvaluationTriggerSource>(r.GetString(r.GetOrdinal("TriggerSource")), out var t)
            ? t : EvaluationTriggerSource.Manual;

        return new EvaluationRun(
            RunId: r.GetString(r.GetOrdinal("RunId")),
            ProjectId: r.GetString(r.GetOrdinal("ProjectId")),
            AgentDefinitionId: r.GetString(r.GetOrdinal("AgentDefinitionId")),
            AgentVersionId: r.GetString(r.GetOrdinal("AgentVersionId")),
            TestSetVersionId: r.GetString(r.GetOrdinal("TestSetVersionId")),
            EvaluatorConfigVersionId: r.GetString(r.GetOrdinal("EvaluatorConfigVersionId")),
            BaselineRunId: GetNullableString(r.GetOrdinal("BaselineRunId")),
            Status: status,
            Priority: r.GetInt32(r.GetOrdinal("Priority")),
            TriggeredBy: GetNullableString(r.GetOrdinal("TriggeredBy")),
            TriggerSource: trigger,
            TriggerContext: triggerContextRaw is null ? null : JsonDocument.Parse(triggerContextRaw),
            ExecutionId: r.GetString(r.GetOrdinal("ExecutionId")),
            CasesTotal: r.GetInt32(r.GetOrdinal("CasesTotal")),
            StartedAt: GetNullableDate(r.GetOrdinal("StartedAt")),
            CompletedAt: GetNullableDate(r.GetOrdinal("CompletedAt")),
            LastHeartbeatAt: GetNullableDate(r.GetOrdinal("LastHeartbeatAt")),
            LastError: GetNullableString(r.GetOrdinal("LastError")),
            CreatedAt: r.GetDateTime(r.GetOrdinal("CreatedAt")));
    }

    public async Task<IReadOnlyList<EvaluationRun>> ListStaleRunningAsync(TimeSpan staleAfter, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var threshold = DateTime.UtcNow - staleAfter;
        var rows = await ctx.EvaluationRuns
            .Where(r => r.Status == "Running" && (r.LastHeartbeatAt == null || r.LastHeartbeatAt < threshold))
            .ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    private static EvaluationRun ToDomain(EvaluationRunRow row)
    {
        var status = Enum.TryParse<EvaluationRunStatus>(row.Status, out var s) ? s : EvaluationRunStatus.Pending;
        var trigger = Enum.TryParse<EvaluationTriggerSource>(row.TriggerSource, out var t) ? t : EvaluationTriggerSource.Manual;
        var triggerContext = string.IsNullOrEmpty(row.TriggerContext) ? null : JsonDocument.Parse(row.TriggerContext);

        return new EvaluationRun(
            RunId: row.RunId,
            ProjectId: row.ProjectId,
            AgentDefinitionId: row.AgentDefinitionId,
            AgentVersionId: row.AgentVersionId,
            TestSetVersionId: row.TestSetVersionId,
            EvaluatorConfigVersionId: row.EvaluatorConfigVersionId,
            BaselineRunId: row.BaselineRunId,
            Status: status,
            Priority: row.Priority,
            TriggeredBy: row.TriggeredBy,
            TriggerSource: trigger,
            TriggerContext: triggerContext,
            ExecutionId: row.ExecutionId,
            CasesTotal: row.CasesTotal,
            StartedAt: row.StartedAt,
            CompletedAt: row.CompletedAt,
            LastHeartbeatAt: row.LastHeartbeatAt,
            LastError: row.LastError,
            CreatedAt: row.CreatedAt);
    }
}
