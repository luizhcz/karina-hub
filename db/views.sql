-- =============================================================================
-- EfsAiHub — Materialized Views
-- PostgreSQL 16+ · Schema: aihub
--
-- Executar APÓS schema.sql.
-- Uso:
--   psql -U <usuario> -d <banco> -f views.sql
--
-- Para refresh manual das views:
--   REFRESH MATERIALIZED VIEW CONCURRENTLY aihub.v_llm_cost;
--   REFRESH MATERIALIZED VIEW CONCURRENTLY aihub.mv_execution_stats_hourly;
--   REFRESH MATERIALIZED VIEW CONCURRENTLY aihub.mv_token_usage_hourly;
-- =============================================================================

SET search_path TO aihub;

-- =============================================================================
-- 1. v_llm_cost — custo estimado por chamada LLM
-- =============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS aihub.v_llm_cost AS
SELECT
    u."Id",
    u."AgentId",
    u."ModelId",
    u."ExecutionId",
    u."WorkflowId",
    u."InputTokens",
    u."OutputTokens",
    u."TotalTokens",
    u."DurationMs",
    u."CreatedAt",
    COALESCE(p."PricePerInputToken"  * u."InputTokens" +
             p."PricePerOutputToken" * u."OutputTokens", 0) AS "EstimatedCostUsd"
FROM aihub.llm_token_usage u
LEFT JOIN LATERAL (
    SELECT mp."PricePerInputToken", mp."PricePerOutputToken"
    FROM aihub.model_pricing mp
    WHERE mp."ModelId" = u."ModelId"
      AND mp."EffectiveFrom" <= u."CreatedAt"
      AND (mp."EffectiveTo" IS NULL OR mp."EffectiveTo" > u."CreatedAt")
    ORDER BY mp."EffectiveFrom" DESC
    LIMIT 1
) p ON true;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_v_llm_cost_Id"
    ON aihub.v_llm_cost ("Id");

-- =============================================================================
-- 2. mv_execution_stats_hourly — estatísticas de execução por hora
-- =============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS aihub.mv_execution_stats_hourly AS
SELECT
    date_trunc('hour', "StartedAt")   AS bucket,
    "WorkflowId"                       AS workflow_id,
    "Status"                           AS status,
    COUNT(*)                           AS total,
    COALESCE(AVG(EXTRACT(EPOCH FROM ("CompletedAt" - "StartedAt")) * 1000.0)
        FILTER (WHERE "CompletedAt" IS NOT NULL), 0) AS avg_ms,
    COALESCE(percentile_cont(0.5) WITHIN GROUP (
        ORDER BY EXTRACT(EPOCH FROM ("CompletedAt" - "StartedAt")) * 1000.0)
        FILTER (WHERE "CompletedAt" IS NOT NULL), 0) AS p50_ms,
    COALESCE(percentile_cont(0.95) WITHIN GROUP (
        ORDER BY EXTRACT(EPOCH FROM ("CompletedAt" - "StartedAt")) * 1000.0)
        FILTER (WHERE "CompletedAt" IS NOT NULL), 0) AS p95_ms
FROM aihub.workflow_executions
GROUP BY bucket, workflow_id, status;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_mv_execution_stats_hourly_bucket_wf_status"
    ON aihub.mv_execution_stats_hourly (bucket, workflow_id, status);

-- =============================================================================
-- 3. mv_token_usage_hourly — uso de tokens agregado por hora
-- =============================================================================

CREATE MATERIALIZED VIEW IF NOT EXISTS aihub.mv_token_usage_hourly AS
SELECT
    date_trunc('hour', u."CreatedAt") AS bucket,
    u."AgentId"                        AS agent_id,
    u."ModelId"                        AS model_id,
    SUM(u."InputTokens")               AS input_tokens,
    SUM(u."OutputTokens")              AS output_tokens,
    SUM(u."TotalTokens")               AS total_tokens,
    COUNT(*)                           AS calls,
    COALESCE(SUM(
        COALESCE(p."PricePerInputToken"  * u."InputTokens" +
                 p."PricePerOutputToken" * u."OutputTokens", 0)
    ), 0) AS estimated_cost_usd
FROM aihub.llm_token_usage u
LEFT JOIN LATERAL (
    SELECT mp."PricePerInputToken", mp."PricePerOutputToken"
    FROM aihub.model_pricing mp
    WHERE mp."ModelId" = u."ModelId"
      AND mp."EffectiveFrom" <= u."CreatedAt"
      AND (mp."EffectiveTo" IS NULL OR mp."EffectiveTo" > u."CreatedAt")
    ORDER BY mp."EffectiveFrom" DESC
    LIMIT 1
) p ON true
GROUP BY bucket, agent_id, model_id;

CREATE UNIQUE INDEX IF NOT EXISTS "IX_mv_token_usage_hourly_bucket_agent_model"
    ON aihub.mv_token_usage_hourly (bucket, agent_id, model_id);

-- =============================================================================
-- FIM DAS VIEWS
-- =============================================================================
