using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

public class PgLlmTokenUsageRepository : ILlmTokenUsageRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    public PgLlmTokenUsageRepository(IDbContextFactory<AgentFwDbContext> factory)
        => _factory = factory;

    public async Task AppendAsync(LlmTokenUsage usage, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.LlmTokenUsages.Add(new LlmTokenUsageRow
        {
            AgentId = usage.AgentId,
            ModelId = usage.ModelId,
            ExecutionId = usage.ExecutionId,
            WorkflowId = usage.WorkflowId,
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
            DurationMs = usage.DurationMs,
            PromptVersionId = usage.PromptVersionId,
            AgentVersionId = usage.AgentVersionId,
            OutputContent = usage.OutputContent,
            RetryCount = usage.RetryCount,
            CreatedAt = usage.CreatedAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<LlmTokenUsage>> GetByExecutionIdAsync(string executionId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.LlmTokenUsages.AsNoTracking()
            .Where(r => r.ExecutionId == executionId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<LlmTokenUsage>> GetByAgentIdAsync(string agentId, int limit = 100, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.LlmTokenUsages.AsNoTracking()
            .Where(r => r.AgentId == agentId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
        return rows.Select(MapToDomain).ToList();
    }

    public async Task<AgentTokenSummary> GetAgentSummaryAsync(string agentId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var summary = await db.LlmTokenUsages.AsNoTracking()
            .Where(r => r.AgentId == agentId && r.CreatedAt >= from && r.CreatedAt <= to)
            .GroupBy(_ => 1)
            .Select(g => new AgentTokenSummary
            {
                AgentId = agentId,
                ModelId = g.Max(r => r.ModelId) ?? "",
                TotalInput = g.Sum(r => (long)r.InputTokens),
                TotalOutput = g.Sum(r => (long)r.OutputTokens),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                CallCount = g.Count(),
                AvgDurationMs = g.Average(r => r.DurationMs)
            })
            .FirstOrDefaultAsync(ct);

        return summary ?? new AgentTokenSummary { AgentId = agentId };
    }

    public async Task<IReadOnlyList<AgentTokenSummary>> GetAllAgentsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.LlmTokenUsages.AsNoTracking()
            .Where(r => r.CreatedAt >= from && r.CreatedAt <= to)
            .GroupBy(r => new { r.AgentId, r.ModelId })
            .Select(g => new AgentTokenSummary
            {
                AgentId = g.Key.AgentId,
                ModelId = g.Key.ModelId,
                TotalInput = g.Sum(r => (long)r.InputTokens),
                TotalOutput = g.Sum(r => (long)r.OutputTokens),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                CallCount = g.Count(),
                AvgDurationMs = g.Average(r => r.DurationMs)
            })
            .OrderByDescending(s => s.TotalTokens)
            .ToListAsync(ct);
    }

    public async Task<GlobalTokenSummary> GetGlobalSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var byAgent = await GetAllAgentsSummaryAsync(from, to, ct);
        return new GlobalTokenSummary
        {
            TotalInput = byAgent.Sum(a => a.TotalInput),
            TotalOutput = byAgent.Sum(a => a.TotalOutput),
            TotalTokens = byAgent.Sum(a => a.TotalTokens),
            TotalCalls = byAgent.Sum(a => a.CallCount),
            AvgDurationMs = byAgent.Count > 0 ? byAgent.Average(a => a.AvgDurationMs) : 0,
            ByAgent = byAgent
        };
    }

    public async Task<ThroughputResult> GetThroughputAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Throughput de tokens por hora (de llm_token_usage)
        var tokenBuckets = await db.Database
            .SqlQueryRaw<TokenBucketRaw>("""
                SELECT
                    date_trunc('hour', "CreatedAt") AS "Bucket",
                    SUM("TotalTokens")::bigint       AS "Tokens",
                    COUNT(*)::int                     AS "LlmCalls",
                    AVG("DurationMs")                 AS "AvgDurationMs"
                FROM llm_token_usage
                WHERE "CreatedAt" >= {0} AND "CreatedAt" <= {1}
                GROUP BY date_trunc('hour', "CreatedAt")
                ORDER BY "Bucket"
                """, from, to)
            .ToListAsync(ct);

        // Throughput de execuções por hora (de workflow_executions)
        var execBuckets = await db.Database
            .SqlQueryRaw<ExecBucketRaw>("""
                SELECT
                    date_trunc('hour', "StartedAt") AS "Bucket",
                    COUNT(*)::int                    AS "Executions"
                FROM workflow_executions
                WHERE "StartedAt" >= {0} AND "StartedAt" <= {1}
                GROUP BY date_trunc('hour', "StartedAt")
                ORDER BY "Bucket"
                """, from, to)
            .ToListAsync(ct);

        var execMap = execBuckets.ToDictionary(e => e.Bucket, e => e.Executions);

        // Combina em buckets unificados
        var allHours = tokenBuckets.Select(t => t.Bucket)
            .Union(execBuckets.Select(e => e.Bucket))
            .Distinct()
            .OrderBy(h => h)
            .ToList();

        var buckets = allHours.Select(h =>
        {
            var tok = tokenBuckets.FirstOrDefault(t => t.Bucket == h);
            execMap.TryGetValue(h, out var execCount);
            return new ThroughputBucket
            {
                Bucket = h,
                Executions = execCount,
                Tokens = tok?.Tokens ?? 0,
                LlmCalls = tok?.LlmCalls ?? 0,
                AvgDurationMs = tok?.AvgDurationMs ?? 0,
            };
        }).ToList();

        var totalHours = Math.Max((to - from).TotalHours, 1);
        return new ThroughputResult
        {
            Buckets = buckets,
            AvgExecutionsPerHour = buckets.Sum(b => b.Executions) / totalHours,
            AvgTokensPerHour = buckets.Sum(b => b.Tokens) / totalHours,
            AvgCallsPerHour = buckets.Sum(b => b.LlmCalls) / totalHours,
        };
    }

    public async Task<IReadOnlyList<WorkflowTokenSummary>> GetAllWorkflowsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Database
            .SqlQueryRaw<WorkflowTokenRaw>("""
                SELECT
                    "WorkflowId",
                    "ModelId",
                    SUM("InputTokens")::bigint  AS "TotalInput",
                    SUM("OutputTokens")::bigint AS "TotalOutput",
                    SUM("TotalTokens")::bigint  AS "TotalTokens",
                    COUNT(*)::int               AS "CallCount",
                    AVG("DurationMs")           AS "AvgDurationMs"
                FROM llm_token_usage
                WHERE "WorkflowId" IS NOT NULL
                  AND "CreatedAt" >= {0} AND "CreatedAt" <= {1}
                GROUP BY "WorkflowId", "ModelId"
                ORDER BY SUM("TotalTokens") DESC
                """, from, to)
            .ToListAsync(ct);

        return rows.Select(r => new WorkflowTokenSummary
        {
            WorkflowId = r.WorkflowId,
            ModelId = r.ModelId,
            TotalInput = r.TotalInput,
            TotalOutput = r.TotalOutput,
            TotalTokens = r.TotalTokens,
            CallCount = r.CallCount,
            AvgDurationMs = r.AvgDurationMs,
        }).ToList();
    }

    public async Task<IReadOnlyList<ProjectTokenSummary>> GetAllProjectsSummaryAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Database
            .SqlQueryRaw<ProjectTokenRaw>("""
                SELECT
                    we."ProjectId",
                    ltu."ModelId",
                    SUM(ltu."InputTokens")::bigint  AS "TotalInput",
                    SUM(ltu."OutputTokens")::bigint AS "TotalOutput",
                    SUM(ltu."TotalTokens")::bigint  AS "TotalTokens",
                    COUNT(*)::int                   AS "CallCount",
                    AVG(ltu."DurationMs")           AS "AvgDurationMs"
                FROM llm_token_usage ltu
                INNER JOIN workflow_executions we ON we."ExecutionId" = ltu."ExecutionId"
                WHERE ltu."CreatedAt" >= {0} AND ltu."CreatedAt" <= {1}
                GROUP BY we."ProjectId", ltu."ModelId"
                ORDER BY SUM(ltu."TotalTokens") DESC
                """, from, to)
            .ToListAsync(ct);

        return rows.Select(r => new ProjectTokenSummary
        {
            ProjectId = r.ProjectId,
            ModelId = r.ModelId,
            TotalInput = r.TotalInput,
            TotalOutput = r.TotalOutput,
            TotalTokens = r.TotalTokens,
            CallCount = r.CallCount,
            AvgDurationMs = r.AvgDurationMs,
        }).ToList();
    }

    // Tipos de projeção para queries SQL brutas
    private class TokenBucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Tokens { get; set; }
        public int LlmCalls { get; set; }
        public double AvgDurationMs { get; set; }
    }

    private class ExecBucketRaw
    {
        public DateTime Bucket { get; set; }
        public int Executions { get; set; }
    }

    private class WorkflowTokenRaw
    {
        public string WorkflowId { get; set; } = "";
        public string ModelId { get; set; } = "";
        public long TotalInput { get; set; }
        public long TotalOutput { get; set; }
        public long TotalTokens { get; set; }
        public int CallCount { get; set; }
        public double AvgDurationMs { get; set; }
    }

    private class ProjectTokenRaw
    {
        public string ProjectId { get; set; } = "";
        public string ModelId { get; set; } = "";
        public long TotalInput { get; set; }
        public long TotalOutput { get; set; }
        public long TotalTokens { get; set; }
        public int CallCount { get; set; }
        public double AvgDurationMs { get; set; }
    }

    private static LlmTokenUsage MapToDomain(LlmTokenUsageRow row) => new()
    {
        Id = row.Id,
        AgentId = row.AgentId,
        ModelId = row.ModelId,
        ExecutionId = row.ExecutionId,
        WorkflowId = row.WorkflowId,
        InputTokens = row.InputTokens,
        OutputTokens = row.OutputTokens,
        TotalTokens = row.TotalTokens,
        DurationMs = row.DurationMs,
        PromptVersionId = row.PromptVersionId,
        AgentVersionId = row.AgentVersionId,
        OutputContent = row.OutputContent,
        RetryCount = row.RetryCount,
        CreatedAt = row.CreatedAt
    };
}
