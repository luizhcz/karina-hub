using EfsAiHub.Core.Abstractions.Observability;
using Microsoft.EntityFrameworkCore;

namespace EfsAiHub.Infra.Persistence.Postgres;

/// <summary>
/// Analytics de decisões de Router via query em <c>workflow_event_audit</c>.
/// Não cria tabela nova — o telemetria já loga <c>node_completed</c> com
/// payload contendo <c>output</c> JSON estruturado (intent, confidence,
/// reason) quando <c>agentType='Router'</c>.
///
/// Estratégia de extração:
///   1. CTE filtra rows: node_completed + Router agentType + output que parece JSON
///      (<c>LIKE '{%'</c> — descarta ~3% que vêm prefixados com "Assistant: ...").
///   2. Cast aninhado: <c>Payload::jsonb ->> 'output'</c> devolve string;
///      <c>::jsonb</c> parseia como objeto; <c>->></c> extrai intent/confidence.
///   3. JOIN com <c>v_production_executions</c> filtra sandbox e amarra ProjectId.
///
/// Custo: O(rows com Timestamp no range) — IX_workflow_event_audit_Timestamp
/// cobre o predicate principal. Parseio JSON acontece sobre o subset filtrado.
/// Em volumes altos (centenas de milhares/mês), considerar cache Redis ou
/// índice expression (GIN parcial no Payload).
/// </summary>
public sealed class PgRouterDecisionAnalyticsRepository : IRouterDecisionAnalyticsRepository
{
    private readonly IDbContextFactory<AgentFwDbContext> _factory;

    // Intents reservados que sinalizam ambiguidade (não-decisão útil).
    // Aparecem como intent literal no output do router quando o LLM bate
    // nos guardrails (mensagem fora de escopo, precisa esclarecimento,
    // loop detectado).
    private const string AmbiguityIntentsCondition =
        "intent IN ('out_of_scope', 'needs_clarification', 'loop_guard')";

    // LIKE pattern parametrizado (vai como placeholder {3}) — alternativa
    // ao literal "'{%'" inline, que confundiria o string.Format interno do
    // SqlQueryRaw (vê `{` como início de placeholder mal-formado).
    private const string JsonStartPattern = "{%";

    // Regex patterns parametrizados (placeholders {4}, {5}) pra extrair
    // intent + confidence diretamente do texto JSON, sem cast `::jsonb`.
    // Robusto contra outputs malformados (LLM ocasionalmente devolve JSON
    // com vírgulas órfãs / chaves duplicadas / truncamento) — antes o cast
    // falhava com Postgres 22P02 mesmo após filtrar com LIKE '{%'.
    private const string IntentRegex = @"""intent""\s*:\s*""([^""]+)""";
    private const string ConfidenceRegex = @"""confidence""\s*:\s*([0-9.]+)";

    public PgRouterDecisionAnalyticsRepository(IDbContextFactory<AgentFwDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<RouterDecisionOverview> GetOverviewAsync(
        string projectId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // CTE base: extrai intent + confidence pra cada router decision parseável.
        // Repetida nas 2 queries (agregado + por-intent) porque cada uma faz
        // aggregation diferente; vale evitar materializar tudo numa subquery
        // gigante (Postgres CTE não inline + IS NOT MATERIALIZED do 12+ ajuda
        // mas DRY aqui custa caro).
        // Extração via substring(text FROM 'regex') captura grupo 1 do POSIX
        // pattern. Sem cast `::jsonb` → indiferente a outputs JSON malformados
        // (LLM ocasional) e ao escape `{` que confundia o string.Format.
        const string baseCte = """
            decisions AS (
                SELECT
                    substring((wea."Payload"::jsonb ->> 'output') FROM {4})              AS intent,
                    substring((wea."Payload"::jsonb ->> 'output') FROM {5})::float8      AS confidence,
                    wea."Timestamp"                                                      AS ts
                FROM aihub.workflow_event_audit wea
                INNER JOIN aihub.v_production_executions we ON we."ExecutionId" = wea."ExecutionId"
                WHERE wea."EventType" = 'node_completed'
                  AND wea."Timestamp" BETWEEN {1} AND {2}
                  AND we."ProjectId" = {0}
                  AND wea."Payload"::jsonb ->> 'agentType' = 'Router'
                  AND (wea."Payload"::jsonb ->> 'output') LIKE {3}
            )
            """;

        var aggSql = $$"""
            WITH {{baseCte}}
            SELECT
                COUNT(*)::bigint                                                            AS "TotalDecisions",
                COUNT(DISTINCT intent)::int                                                 AS "DistinctIntents",
                COALESCE(AVG(confidence), 0)::float8                                        AS "AvgConfidence",
                COALESCE(percentile_cont(0.5)  WITHIN GROUP (ORDER BY confidence), 0)::float8 AS "P50Confidence",
                COUNT(*) FILTER (WHERE confidence < 0.5)::bigint                            AS "LowConfidenceCount",
                COUNT(*) FILTER (WHERE {{AmbiguityIntentsCondition}})::bigint               AS "AmbiguityCount"
            FROM decisions
            """;

        // 4º param "{%" é o LIKE pattern — parametrizado pra evitar conflito
        // com string.Format do SqlQueryRaw (literal `{` no SQL viraria
        // placeholder mal-formado).
        var agg = await db.Database.SqlQueryRaw<OverviewAggRaw>(aggSql, projectId, from, to, JsonStartPattern, IntentRegex, ConfidenceRegex).ToListAsync(ct);
        var a = agg.FirstOrDefault() ?? new OverviewAggRaw();

        var intentsSql = $$"""
            WITH {{baseCte}}
            SELECT
                intent                                                                       AS "Intent",
                COUNT(*)::bigint                                                             AS "Count",
                COALESCE(AVG(confidence), 0)::float8                                         AS "AvgConfidence",
                COALESCE(percentile_cont(0.5)  WITHIN GROUP (ORDER BY confidence), 0)::float8 AS "P50Confidence",
                COALESCE(percentile_cont(0.95) WITHIN GROUP (ORDER BY confidence), 0)::float8 AS "P95Confidence",
                COALESCE(MIN(confidence), 0)::float8                                          AS "MinConfidence"
            FROM decisions
            WHERE intent IS NOT NULL
            GROUP BY intent
            ORDER BY COUNT(*) DESC
            LIMIT 25
            """;

        var intents = await db.Database.SqlQueryRaw<IntentRowRaw>(intentsSql, projectId, from, to, JsonStartPattern, IntentRegex, ConfidenceRegex).ToListAsync(ct);

        return new RouterDecisionOverview
        {
            ProjectId = projectId,
            PeriodFrom = from,
            PeriodTo = to,
            TotalDecisions = a.TotalDecisions,
            DistinctIntents = a.DistinctIntents,
            AvgConfidence = a.AvgConfidence,
            P50Confidence = a.P50Confidence,
            LowConfidenceCount = a.LowConfidenceCount,
            LowConfidenceRate = a.TotalDecisions > 0
                ? (double)a.LowConfidenceCount / a.TotalDecisions
                : 0d,
            AmbiguityCount = a.AmbiguityCount,
            AmbiguityRate = a.TotalDecisions > 0
                ? (double)a.AmbiguityCount / a.TotalDecisions
                : 0d,
            Intents = intents.Select(r => new RouterIntentStats
            {
                Intent = r.Intent,
                Count = r.Count,
                AvgConfidence = r.AvgConfidence,
                P50Confidence = r.P50Confidence,
                P95Confidence = r.P95Confidence,
                MinConfidence = r.MinConfidence,
            }).ToArray(),
        };
    }

    public async Task<IReadOnlyList<RouterDecisionTimeseriesBucket>> GetTimeseriesAsync(
        string projectId, DateTime from, DateTime to, string groupBy, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var truncUnit = groupBy?.Trim().ToLowerInvariant() switch
        {
            "hour" => "hour",
            _ => "day",
        };

        var sql = $$"""
            WITH decisions AS (
                SELECT
                    substring((wea."Payload"::jsonb ->> 'output') FROM {4})              AS intent,
                    substring((wea."Payload"::jsonb ->> 'output') FROM {5})::float8      AS confidence,
                    wea."Timestamp"                                                      AS ts
                FROM aihub.workflow_event_audit wea
                INNER JOIN aihub.v_production_executions we ON we."ExecutionId" = wea."ExecutionId"
                WHERE wea."EventType" = 'node_completed'
                  AND wea."Timestamp" BETWEEN {0} AND {1}
                  AND we."ProjectId" = {2}
                  AND wea."Payload"::jsonb ->> 'agentType' = 'Router'
                  AND (wea."Payload"::jsonb ->> 'output') LIKE {3}
            )
            SELECT
                date_trunc('{{truncUnit}}', ts)::timestamptz                                  AS "Bucket",
                COUNT(*)::bigint                                                              AS "Total",
                COALESCE(AVG(confidence), 0)::float8                                          AS "AvgConfidence",
                COUNT(*) FILTER (WHERE confidence < 0.5)::bigint                              AS "LowConfidenceCount",
                COUNT(*) FILTER (WHERE {{AmbiguityIntentsCondition}})::bigint                 AS "AmbiguityCount"
            FROM decisions
            GROUP BY date_trunc('{{truncUnit}}', ts)
            ORDER BY date_trunc('{{truncUnit}}', ts) ASC
            """;

        var rows = await db.Database.SqlQueryRaw<BucketRaw>(sql, from, to, projectId, JsonStartPattern, IntentRegex, ConfidenceRegex).ToListAsync(ct);

        return rows.Select(r => new RouterDecisionTimeseriesBucket
        {
            Bucket = r.Bucket,
            Total = r.Total,
            AvgConfidence = r.AvgConfidence,
            LowConfidenceCount = r.LowConfidenceCount,
            AmbiguityCount = r.AmbiguityCount,
        }).ToArray();
    }

    private sealed class OverviewAggRaw
    {
        public long TotalDecisions { get; set; }
        public int DistinctIntents { get; set; }
        public double AvgConfidence { get; set; }
        public double P50Confidence { get; set; }
        public long LowConfidenceCount { get; set; }
        public long AmbiguityCount { get; set; }
    }

    private sealed class IntentRowRaw
    {
        public string Intent { get; set; } = "";
        public long Count { get; set; }
        public double AvgConfidence { get; set; }
        public double P50Confidence { get; set; }
        public double P95Confidence { get; set; }
        public double MinConfidence { get; set; }
    }

    private sealed class BucketRaw
    {
        public DateTime Bucket { get; set; }
        public long Total { get; set; }
        public double AvgConfidence { get; set; }
        public long LowConfidenceCount { get; set; }
        public long AmbiguityCount { get; set; }
    }
}
