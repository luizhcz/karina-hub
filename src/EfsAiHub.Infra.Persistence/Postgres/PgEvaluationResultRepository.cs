using System.Text.Json;
using EfsAiHub.Core.Agents.Evaluation;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Append batch de results + upsert progress. Counters em tabela auxiliar
/// reduzem lock contention vs UPDATE na hot row do header.
/// </summary>
public sealed class PgEvaluationResultRepository : IEvaluationResultRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgEvaluationResultRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<IReadOnlyList<EvaluationResult>> ListByRunAsync(
        string runId,
        bool? passedFilter = null,
        string? evaluatorNameFilter = null,
        int? skip = null,
        int? take = null,
        CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.EvaluationResults.Where(r => r.RunId == runId);
        if (passedFilter.HasValue) query = query.Where(r => r.Passed == passedFilter.Value);
        if (!string.IsNullOrEmpty(evaluatorNameFilter)) query = query.Where(r => r.EvaluatorName == evaluatorNameFilter);
        query = query.OrderBy(r => r.CaseId).ThenBy(r => r.EvaluatorName).ThenBy(r => r.BindingIndex).ThenBy(r => r.RepetitionIndex);
        if (skip.HasValue) query = query.Skip(skip.Value);
        if (take.HasValue) query = query.Take(take.Value);
        var rows = await query.ToListAsync(ct);
        return rows.Select(ToDomain).ToList();
    }

    public async Task<int> CountByRunAsync(string runId, bool? passedFilter = null, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var query = ctx.EvaluationResults.Where(r => r.RunId == runId);
        if (passedFilter.HasValue) query = query.Where(r => r.Passed == passedFilter.Value);
        return await query.CountAsync(ct);
    }

    public async Task AppendBatchAsync(
        string runId,
        IReadOnlyList<EvaluationResult> results,
        CancellationToken ct = default)
    {
        if (results.Count == 0) return;

        await using var ctx = await _factory.CreateDbContextAsync(ct);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        // Insert ordenado por PK pra evitar deadlock entre workers paralelos
        // que escrevem no mesmo run.
        var ordered = results
            .OrderBy(r => r.CaseId, StringComparer.Ordinal)
            .ThenBy(r => r.EvaluatorName, StringComparer.Ordinal)
            .ThenBy(r => r.BindingIndex)
            .ThenBy(r => r.RepetitionIndex);

        foreach (var r in ordered)
        {
            ctx.EvaluationResults.Add(new EvaluationResultRow
            {
                ResultId = r.ResultId,
                RunId = r.RunId,
                CaseId = r.CaseId,
                EvaluatorName = r.EvaluatorName,
                BindingIndex = r.BindingIndex,
                RepetitionIndex = r.RepetitionIndex,
                Score = r.Score,
                Passed = r.Passed,
                Reason = r.Reason,
                OutputContent = r.OutputContent,
                JudgeModel = r.JudgeModel,
                LatencyMs = r.LatencyMs,
                CostUsd = r.CostUsd,
                InputTokens = r.InputTokens,
                OutputTokens = r.OutputTokens,
                EvaluatorMetadata = r.EvaluatorMetadata?.RootElement.GetRawText(),
                CreatedAt = r.CreatedAt
            });
        }
        await ctx.SaveChangesAsync(ct);

        // Pass/fail contabilizado por RESULT (não por case): 1 case × N evaluators
        // × M reps = N×M avaliações. CasesCompleted = cases distintos tocados
        // (proxy de progresso pra UI).
        var batchPassed = results.Count(r => r.Passed);
        var batchFailed = results.Count - batchPassed;
        var batchCases = results.Select(r => r.CaseId).Distinct().Count();
        var batchCost = results.Sum(r => r.CostUsd ?? 0m);
        var batchTokens = results.Sum(r => (long)((r.InputTokens ?? 0) + (r.OutputTokens ?? 0)));
        var avgScore = results.Where(r => r.Score.HasValue).Select(r => r.Score!.Value).DefaultIfEmpty(0m).Average();
        var now = DateTime.UtcNow;

        await ctx.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO aihub.evaluation_run_progress
    (""RunId"", ""CasesCompleted"", ""CasesPassed"", ""CasesFailed"", ""AvgScore"",
     ""TotalCostUsd"", ""TotalTokens"", ""LastUpdated"")
VALUES
    ({runId}, {batchCases}, {batchPassed}, {batchFailed}, {avgScore},
     {batchCost}, {batchTokens}, {now}::timestamptz)
ON CONFLICT (""RunId"") DO UPDATE
   SET ""CasesCompleted"" = aihub.evaluation_run_progress.""CasesCompleted"" + EXCLUDED.""CasesCompleted"",
       ""CasesPassed""    = aihub.evaluation_run_progress.""CasesPassed""    + EXCLUDED.""CasesPassed"",
       ""CasesFailed""    = aihub.evaluation_run_progress.""CasesFailed""    + EXCLUDED.""CasesFailed"",
       ""AvgScore""       = CASE
                              WHEN aihub.evaluation_run_progress.""CasesCompleted"" + EXCLUDED.""CasesCompleted"" > 0
                              THEN (
                                  COALESCE(aihub.evaluation_run_progress.""AvgScore"", 0) * aihub.evaluation_run_progress.""CasesCompleted""
                                + EXCLUDED.""AvgScore"" * EXCLUDED.""CasesCompleted""
                              ) / NULLIF(aihub.evaluation_run_progress.""CasesCompleted"" + EXCLUDED.""CasesCompleted"", 0)
                              ELSE aihub.evaluation_run_progress.""AvgScore""
                            END,
       ""TotalCostUsd""   = aihub.evaluation_run_progress.""TotalCostUsd""   + EXCLUDED.""TotalCostUsd"",
       ""TotalTokens""    = aihub.evaluation_run_progress.""TotalTokens""    + EXCLUDED.""TotalTokens"",
       ""LastUpdated""    = EXCLUDED.""LastUpdated""
", ct);

        await tx.CommitAsync(ct);
    }

    public async Task<EvaluationRunProgress?> GetProgressAsync(string runId, CancellationToken ct = default)
    {
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var row = await ctx.EvaluationRunProgress.FindAsync([runId], ct);
        if (row is null) return null;
        return new EvaluationRunProgress(
            RunId: row.RunId,
            CasesCompleted: row.CasesCompleted,
            CasesPassed: row.CasesPassed,
            CasesFailed: row.CasesFailed,
            AvgScore: row.AvgScore,
            TotalCostUsd: row.TotalCostUsd,
            TotalTokens: row.TotalTokens,
            LastUpdated: row.LastUpdated);
    }

    public async Task<EvaluationRunUsage> GetUsageAsync(string runId, CancellationToken ct = default)
    {
        // Tokens agregados via llm_token_usage por ExecutionId='eval:{runId}' —
        // single source of truth do TokenTrackingChatClient (cobre agente sob
        // teste E judges sem duplicação). Pricing ausente → cost=0 (fallback).
        await using var ctx = await _factory.CreateDbContextAsync(ct);
        var executionId = $"eval:{runId}";
        var conn = ctx.Database.GetDbConnection();
        await ctx.Database.OpenConnectionAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            // LEFT JOIN LATERAL garante 1:1 mesmo quando há múltiplas entradas
            // em model_pricing pro mesmo ModelId (ex.: provider OPENAI + AZUREOPENAI
            // pro mesmo deployment). Pega o pricing mais recente vigente — sem
            // ele, o cost/tokens vinha duplicado pela cardinalidade do JOIN.
            cmd.CommandText = @"
SELECT
    COALESCE(SUM(u.""InputTokens""), 0)::bigint  AS in_tokens,
    COALESCE(SUM(u.""OutputTokens""), 0)::bigint AS out_tokens,
    COALESCE(SUM(u.""TotalTokens""), 0)::bigint  AS total_tokens,
    COALESCE(SUM(
        u.""InputTokens""  * COALESCE(mp.""PricePerInputToken"",  0) +
        u.""OutputTokens"" * COALESCE(mp.""PricePerOutputToken"", 0)
    ), 0)::numeric AS total_cost_usd
FROM aihub.llm_token_usage u
LEFT JOIN LATERAL (
    SELECT pp.""PricePerInputToken"", pp.""PricePerOutputToken""
    FROM aihub.model_pricing pp
    WHERE pp.""ModelId"" = u.""ModelId""
      AND pp.""EffectiveFrom"" <= u.""CreatedAt""
      AND (pp.""EffectiveTo"" IS NULL OR pp.""EffectiveTo"" > u.""CreatedAt"")
    ORDER BY pp.""EffectiveFrom"" DESC
    LIMIT 1
) mp ON TRUE
WHERE u.""ExecutionId"" = @executionId
";
            var p = cmd.CreateParameter();
            p.ParameterName = "executionId";
            p.Value = executionId;
            cmd.Parameters.Add(p);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return new EvaluationRunUsage(0, 0, 0, 0m);

            return new EvaluationRunUsage(
                InputTokens: reader.GetInt64(0),
                OutputTokens: reader.GetInt64(1),
                TotalTokens: reader.GetInt64(2),
                TotalCostUsd: reader.GetDecimal(3));
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static EvaluationResult ToDomain(EvaluationResultRow row)
    {
        JsonDocument? metadata = null;
        if (!string.IsNullOrEmpty(row.EvaluatorMetadata))
            metadata = JsonDocument.Parse(row.EvaluatorMetadata);

        return new EvaluationResult(
            ResultId: row.ResultId,
            RunId: row.RunId,
            CaseId: row.CaseId,
            EvaluatorName: row.EvaluatorName,
            BindingIndex: row.BindingIndex,
            RepetitionIndex: row.RepetitionIndex,
            Score: row.Score,
            Passed: row.Passed,
            Reason: row.Reason,
            OutputContent: row.OutputContent,
            JudgeModel: row.JudgeModel,
            LatencyMs: row.LatencyMs,
            CostUsd: row.CostUsd,
            InputTokens: row.InputTokens,
            OutputTokens: row.OutputTokens,
            EvaluatorMetadata: metadata,
            CreatedAt: row.CreatedAt);
    }
}
