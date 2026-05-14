using EfsAiHub.Core.Abstractions.Observability;
using EfsAiHub.Infra.Persistence.Cache;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Implementação Postgres de <see cref="IProjectAnalyticsRepository"/>.
/// Padrão: SQL raw via <c>db.Database.SqlQueryRaw</c>, mesma estratégia do
/// <see cref="PgLlmTokenUsageRepository"/>. Reaproveita a matview
/// <c>aihub.v_llm_cost</c> (refresh a cada 30min) e <c>workflow_executions</c>.
///
/// JOIN pivô é por <c>ExecutionId</c> — a coluna ProjectId em
/// <c>llm_token_usage</c> é nullable em rows legadas (pré-fix do AsyncLocal),
/// mas <c>workflow_executions.ProjectId</c> é NOT NULL e canônico.
///
/// ProjectId chega validado pelo controller (defesa em profundidade aqui é
/// só parametrização — cross-project não passa do controller via helper de
/// auth, mas se passar, o WHERE ainda filtra corretamente).
/// </summary>
public sealed class PgProjectAnalyticsRepository : IProjectAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;
    private readonly IEfsRedisCache _cache;

    public PgProjectAnalyticsRepository(
        IDbContextFactory<AgentFwDbContext> factory,
        IEfsRedisCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    public async Task<ProjectOverview> GetProjectOverviewAsync(
        string projectId, DateTime from, DateTime to, bool ownedOnly = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Quando ownedOnly=true precisamos do AgentId (via llm_token_usage) e do
        // ProjectId do agent (via agent_definitions). Caso contrário mantemos a
        // query original sem o JOIN extra (mais barata + cobre rows sem AgentId).
        var llmJoin = ownedOnly
            ? @"INNER JOIN aihub.llm_token_usage ltu ON ltu.""Id"" = c.""Id""
                INNER JOIN aihub.agent_definitions ad ON ad.""Id"" = ltu.""AgentId"""
            : "";
        var llmExtraWhere = ownedOnly ? @" AND ad.""ProjectId"" = {0}" : "";

        string sql = $@"
            SELECT
                COALESCE(SUM(c.""EstimatedCostUsd""), 0)::numeric AS ""CostUsd"",
                COALESCE(SUM(c.""TotalTokens""), 0)::bigint        AS ""Tokens"",
                COUNT(*)::int                                      AS ""Calls""
            FROM aihub.v_llm_cost c
            INNER JOIN aihub.v_production_executions we ON we.""ExecutionId"" = c.""ExecutionId""
            {llmJoin}
            WHERE we.""ProjectId"" = {{0}}
              AND c.""CreatedAt"" BETWEEN {{1}} AND {{2}}
              {llmExtraWhere}
            "; 

        var llmStats = await db.Database.SqlQueryRaw<LlmAggRaw>(sql, projectId, from, to)
            .ToListAsync(ct);

        // Stats execução (total/completed/failed) — direto de workflow_executions.
        // Granularidade workflow ≠ agent: ownedOnly NÃO afeta esse bloco (mesma
        // semântica documentada em GetProjectTimeseriesAsync).
        var execStats = await db.Database.SqlQueryRaw<ExecAggRaw>("""
            SELECT
                COUNT(*)::int                                          AS "Total",
                COUNT(*) FILTER (WHERE "Status" = 'Completed')::int    AS "Completed",
                COUNT(*) FILTER (WHERE "Status" = 'Failed')::int       AS "Failed"
            FROM aihub.v_production_executions
            WHERE "ProjectId" = {0}
              AND "StartedAt" BETWEEN {1} AND {2}
            """, projectId, from, to)
            .ToListAsync(ct);

        // Top 3 agentes por custo no período. Hidrata AgentName via LEFT JOIN
        // com agent_definitions — usado pelo dashboard pra exibir "Cuide de
        // Boletas" em vez de "agente-boleta-cliente". LEFT JOIN tolera agentes
        // deletados (AgentName=null, frontend faz fallback pro AgentId).
        var topAgentNameJoin = @"LEFT JOIN aihub.agent_definitions ad ON ad.""Id"" = ltu.""AgentId""";
        var topAgentExtraWhere = ownedOnly ? @" AND ad.""ProjectId"" = {0}" : "";

        var topAgentSql = $@"
            SELECT
                ltu.""AgentId""                                AS ""AgentId"",
                MAX(ad.""Name"")                                AS ""AgentName"",
                COALESCE(SUM(c.""EstimatedCostUsd""), 0)::numeric AS ""CostUsd"",
                COALESCE(SUM(c.""TotalTokens""), 0)::bigint    AS ""Tokens"",
                COUNT(*)::int                                AS ""Calls""
            FROM aihub.v_llm_cost c
            INNER JOIN aihub.llm_token_usage ltu ON ltu.""Id"" = c.""Id""
            INNER JOIN aihub.v_production_executions we ON we.""ExecutionId"" = c.""ExecutionId""
            {topAgentNameJoin}
            WHERE we.""ProjectId"" = {{0}}
              AND c.""CreatedAt"" BETWEEN {{1}} AND {{2}}
              {topAgentExtraWhere}
            GROUP BY ltu.""AgentId""
            ORDER BY SUM(c.""EstimatedCostUsd"") DESC
            LIMIT 3
            ";

        var topAgentRows = await db.Database.SqlQueryRaw<AgentMiniRaw>(topAgentSql, projectId, from, to)
            .ToListAsync(ct);

        var llm = llmStats.FirstOrDefault() ?? new LlmAggRaw();
        var exec = execStats.FirstOrDefault() ?? new ExecAggRaw();
        var resolved = exec.Completed + exec.Failed;

        return new ProjectOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            TotalCostUsd = llm.CostUsd,
            TotalTokens = llm.Tokens,
            TotalCalls = llm.Calls,
            TotalExecutions = exec.Total,
            Completed = exec.Completed,
            Failed = exec.Failed,
            SuccessRate = resolved > 0 ? (double)exec.Completed / resolved : 0d,
            TopAgents = topAgentRows.Select(r => new AgentMiniRow
            {
                AgentId = r.AgentId,
                AgentName = r.AgentName,
                CostUsd = r.CostUsd,
                TotalTokens = r.Tokens,
                Calls = r.Calls
            }).ToList()
        };
    }

    public async Task<IReadOnlyList<ProjectTimeseriesBucket>> GetProjectTimeseriesAsync(
        string projectId,
        DateTime from,
        DateTime to,
        string groupBy,
        IReadOnlyCollection<string>? excludeAgentIds = null,
        bool ownedOnly = false,
        CancellationToken ct = default)
    {
        // Defensivo: groupBy só aceita day/hour pra evitar SQL injection no
        // date_trunc (não é parametrizável em prepared statement). Default = day.
        var trunc = string.Equals(groupBy, "hour", StringComparison.OrdinalIgnoreCase) ? "hour" : "day";

        await using var db = await _factory.CreateDbContextAsync(ct);

        // LLM agrega custo/tokens/calls por bucket. Filtros opcionais por agent:
        // - excludeAgentIds: drop rows com AgentId nessa lista.
        // - ownedOnly: mantém só rows cujo agent pertence ao próprio ProjectId
        //   (JOIN com agent_definitions). Combinável com excludeAgentIds.
        // ltu fica acoplado quando qualquer um dos dois filtros está ativo —
        // INNER JOIN se ownedOnly (precisa do agent dono); LEFT se só exclude
        // (rows sem AgentId continuam contando).
        // Exec agrega workflow_executions — granularidade workflow ≠ agent,
        // não é filtrável por AgentId aqui.
        var hasExclusion = excludeAgentIds is { Count: > 0 };
        var needsLtuJoin = hasExclusion || ownedOnly;
        var ltuJoinKind = ownedOnly ? "INNER" : "LEFT";

        var parameters = new List<object> { projectId, from, to };
        var llmSql = @"
            SELECT
                date_trunc('" + trunc + @"', c.""CreatedAt"")              AS ""Bucket"",
                COALESCE(SUM(c.""EstimatedCostUsd""), 0)::numeric          AS ""CostUsd"",
                COALESCE(SUM(c.""TotalTokens""), 0)::bigint                AS ""Tokens"",
                COUNT(*)::int                                              AS ""Calls""
            FROM aihub.v_llm_cost c
            INNER JOIN aihub.v_production_executions we ON we.""ExecutionId"" = c.""ExecutionId""";
        if (needsLtuJoin)
        {
            llmSql += $@"
            {ltuJoinKind} JOIN aihub.llm_token_usage ltu ON ltu.""Id"" = c.""Id""";
        }
        if (ownedOnly)
        {
            llmSql += @"
            INNER JOIN aihub.agent_definitions ad ON ad.""Id"" = ltu.""AgentId""";
        }
        llmSql += @"
            WHERE we.""ProjectId"" = {0}
              AND c.""CreatedAt"" BETWEEN {1} AND {2}";
        if (ownedOnly)
        {
            // Reusa {0} (projectId) — agent dono == projeto do request.
            llmSql += @"
              AND ad.""ProjectId"" = {0}";
        }
        if (hasExclusion)
        {
            // Parametrizamos como text[] e usamos NOT = ANY — Npgsql serializa
            // List<string> como text[] sem precisar de várias posições.
            var paramIdx = parameters.Count;
            llmSql += $@"
              AND (ltu.""AgentId"" IS NULL OR NOT (ltu.""AgentId"" = ANY({{{paramIdx}}})))";
            parameters.Add(excludeAgentIds!.ToArray());
        }
        llmSql += @"
            GROUP BY 1
            ORDER BY 1";

        var execSql = @"
            SELECT
                date_trunc('" + trunc + @"', ""StartedAt"")                AS ""Bucket"",
                COUNT(*)::int                                              AS ""Executions"",
                COUNT(*) FILTER (WHERE ""Status"" = 'Completed')::int      AS ""Completed"",
                COUNT(*) FILTER (WHERE ""Status"" = 'Failed')::int         AS ""Failed""
            FROM aihub.v_production_executions
            WHERE ""ProjectId"" = {0}
              AND ""StartedAt"" BETWEEN {1} AND {2}
            GROUP BY 1
            ORDER BY 1";

        // Sequencial: o mesmo DbContext não suporta 2 queries simultâneas
        // (EF lança ConcurrencyDetector). Tempo agregado fica na escala dos
        // 2 queries somados — aceitável pra MVP, não otimizamos prematuramente.
        var llmBuckets = await db.Database.SqlQueryRaw<LlmBucketRaw>(llmSql, parameters.ToArray()).ToListAsync(ct);
        var execBuckets = await db.Database.SqlQueryRaw<ExecBucketRaw>(execSql, projectId, from, to).ToListAsync(ct);

        // Merge por bucket — UNION das chaves (se bucket teve LLM mas não execution
        // ou vice-versa, ainda aparece no resultado com 0 do lado faltante).
        var byBucket = new Dictionary<DateTime, (LlmBucketRaw? Llm, ExecBucketRaw? Exec)>();
        foreach (var l in llmBuckets) byBucket[l.Bucket] = (l, byBucket.GetValueOrDefault(l.Bucket).Exec);
        foreach (var e in execBuckets) byBucket[e.Bucket] = (byBucket.GetValueOrDefault(e.Bucket).Llm, e);

        return byBucket
            .OrderBy(kv => kv.Key)
            .Select(kv => new ProjectTimeseriesBucket
            {
                Bucket = kv.Key,
                CostUsd = kv.Value.Llm?.CostUsd ?? 0m,
                Tokens = kv.Value.Llm?.Tokens ?? 0,
                Calls = kv.Value.Llm?.Calls ?? 0,
                Executions = kv.Value.Exec?.Executions ?? 0,
                Completed = kv.Value.Exec?.Completed ?? 0,
                Failed = kv.Value.Exec?.Failed ?? 0
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ProjectAgentBreakdown>> GetProjectAgentBreakdownAsync(
        string projectId, DateTime from, DateTime to, int top, bool ownedOnly = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // ownedOnly = true: JOIN com agent_definitions e filtra
        // ad.ProjectId = projectId nas duas CTEs (mantém consistência entre
        // calls e error_rate). Sem o JOIN o agent_calls captura também
        // chamadas de agentes Visibility=global de outros projetos.
        var ownedJoin = ownedOnly
            ? @"INNER JOIN aihub.agent_definitions ad ON ad.""Id"" = ltu.""AgentId"""
            : "";
        var ownedWhere = ownedOnly ? @" AND ad.""ProjectId"" = {0}" : "";

        // CTE com 2 partes: agent_calls (custo/tokens/duration por chamada do
        // agente) e agent_error_rate (% de execuções distintas envolvendo o
        // agente que terminaram em Failed). LEFT JOIN garante 0 quando não
        // há execução resolvida.

        // LEFT JOIN com agent_definitions na projeção final pra hidratar
        // AgentName em todos os modos (ownedOnly ou não). Tolerante a agent
        // deletado pós-cleanup: AgentName=null → frontend faz fallback pro Id.
        var sql = $@"
            WITH agent_calls AS (
                SELECT ltu.""AgentId"", ltu.""ModelId"", ltu.""ExecutionId"",
                       ltu.""DurationMs"", c.""EstimatedCostUsd"", c.""TotalTokens""
                FROM aihub.v_llm_cost c
                INNER JOIN aihub.llm_token_usage ltu ON ltu.""Id"" = c.""Id""
                INNER JOIN aihub.v_production_executions we ON we.""ExecutionId"" = c.""ExecutionId""
                {ownedJoin}
                WHERE we.""ProjectId"" = {{0}}
                  AND c.""CreatedAt"" BETWEEN {{1}} AND {{2}}
                  {ownedWhere}
            ),
            agent_exec_status AS (
                SELECT DISTINCT ltu.""AgentId"", we.""ExecutionId"", we.""Status""
                FROM aihub.llm_token_usage ltu
                INNER JOIN aihub.v_production_executions we ON we.""ExecutionId"" = ltu.""ExecutionId""
                {ownedJoin}
                WHERE we.""ProjectId"" = {{0}}
                  AND ltu.""CreatedAt"" BETWEEN {{1}} AND {{2}}
                  AND we.""Status"" IN ('Completed', 'Failed')
                  {ownedWhere}
            ),
            agent_error_rate AS (
                SELECT ""AgentId"",
                       (COUNT(*) FILTER (WHERE ""Status"" = 'Failed'))::double precision
                       / NULLIF(COUNT(*), 0)::double precision AS ""ErrorRate""
                FROM agent_exec_status
                GROUP BY ""AgentId""
            )
            SELECT ac.""AgentId""                                                  AS ""AgentId"",
                   MAX(adn.""Name"")                                                AS ""AgentName"",
                   MAX(ac.""ModelId"")                                              AS ""ModelId"",
                   COUNT(*)::int                                                    AS ""Calls"",
                   COALESCE(SUM(ac.""TotalTokens""), 0)::bigint                     AS ""TotalTokens"",
                   COALESCE(SUM(ac.""EstimatedCostUsd""), 0)::numeric               AS ""CostUsd"",
                   COALESCE(AVG(ac.""DurationMs""), 0)::double precision            AS ""AvgDurationMs"",
                   COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY ac.""DurationMs""), 0)::double precision AS ""P95DurationMs"",
                   COALESCE(MAX(aer.""ErrorRate""), 0)::double precision            AS ""ErrorRate""
            FROM agent_calls ac
            LEFT JOIN agent_error_rate aer ON aer.""AgentId"" = ac.""AgentId""
            LEFT JOIN aihub.agent_definitions adn ON adn.""Id"" = ac.""AgentId""
            GROUP BY ac.""AgentId""
            ORDER BY SUM(ac.""EstimatedCostUsd"") DESC
            LIMIT {{3}}
            ";

        var rows = await db.Database.SqlQueryRaw<AgentBreakdownRaw>(sql, projectId, from, to, top)
            .ToListAsync(ct);

        return rows.Select(r => new ProjectAgentBreakdown
        {
            AgentId = r.AgentId,
            AgentName = r.AgentName,
            ModelId = r.ModelId,
            Calls = r.Calls,
            TotalTokens = r.TotalTokens,
            CostUsd = r.CostUsd,
            AvgDurationMs = r.AvgDurationMs,
            P95DurationMs = r.P95DurationMs,
            ErrorRate = r.ErrorRate
        }).ToList();
    }

    public async Task<ProjectBudgetStatus> GetProjectBudgetStatusAsync(
        string projectId, int? maxTokensPerDay, decimal? maxCostUsdPerDay, CancellationToken ct = default)
    {
        // Mesma chave que ProjectBudgetGuard usa pra incrementar.
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var tokensStr = await _cache.GetStringAsync($"budget:tokens:{projectId}:{today}");
        var costStr = await _cache.GetStringAsync($"budget:cost:{projectId}:{today}");

        long todayTokens = long.TryParse(tokensStr, out var t) ? t : 0;
        decimal todayCost = decimal.TryParse(costStr,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var c) ? c : 0m;

        double? tokensUsagePct = maxTokensPerDay is > 0
            ? (double)todayTokens / maxTokensPerDay.Value
            : null;
        double? costUsagePct = maxCostUsdPerDay is > 0
            ? (double)(todayCost / maxCostUsdPerDay.Value)
            : null;

        var exceeded =
            (maxTokensPerDay is > 0 && todayTokens >= maxTokensPerDay.Value) ||
            (maxCostUsdPerDay is > 0 && todayCost >= maxCostUsdPerDay.Value);

        return new ProjectBudgetStatus
        {
            ProjectId = projectId,
            MaxTokensPerDay = maxTokensPerDay,
            MaxCostUsdPerDay = maxCostUsdPerDay,
            TodayTokens = todayTokens,
            TodayCostUsd = todayCost,
            TokensUsagePct = tokensUsagePct,
            CostUsagePct = costUsagePct,
            Exceeded = exceeded
        };
    }

    // Tipos de projeção pra SQL raw.
    private class LlmAggRaw
    {
        public decimal CostUsd { get; set; }
        public long Tokens { get; set; }
        public int Calls { get; set; }
    }

    private class ExecAggRaw
    {
        public int Total { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
    }

    private class AgentMiniRaw
    {
        public string AgentId { get; set; } = "";
        public string? AgentName { get; set; }
        public decimal CostUsd { get; set; }
        public long Tokens { get; set; }
        public int Calls { get; set; }
    }

    private class LlmBucketRaw
    {
        public DateTime Bucket { get; set; }
        public decimal CostUsd { get; set; }
        public long Tokens { get; set; }
        public int Calls { get; set; }
    }

    private class ExecBucketRaw
    {
        public DateTime Bucket { get; set; }
        public int Executions { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
    }

    private class AgentBreakdownRaw
    {
        public string AgentId { get; set; } = "";
        public string? AgentName { get; set; }
        public string? ModelId { get; set; }
        public int Calls { get; set; }
        public long TotalTokens { get; set; }
        public decimal CostUsd { get; set; }
        public double AvgDurationMs { get; set; }
        public double P95DurationMs { get; set; }
        public double ErrorRate { get; set; }
    }
}
