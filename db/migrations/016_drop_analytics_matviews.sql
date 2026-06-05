-- =============================================================================
-- 016_drop_analytics_matviews.sql
--
-- Remove as três materialized views de analytics. O cálculo de
-- EstimatedCostUsd agora é feito em runtime via CTE no
-- PgProjectAnalyticsRepository (LATERAL JOIN com model_pricing), com cache
-- Redis (TTL 30min, invalidação via POST /analytics/projects/{id}/refresh).
--
-- Motivação: matviews exigem privilege especial pra REFRESH MATERIALIZED VIEW,
-- que muitos usuários do banco não têm. Mover pra código + cache Redis
-- elimina essa dependência.
--
-- - v_llm_cost: era a única matview realmente consumida pelo código.
-- - mv_execution_stats_hourly / mv_token_usage_hourly: matviews órfãs (nenhum
--   código fazia SELECT nelas — só o LlmCostRefreshService tentava refresh).
--
-- v_production_executions (view regular, sem refresh) é mantida.
--
-- Idempotente: DROP IF EXISTS.
-- =============================================================================

BEGIN;

DROP MATERIALIZED VIEW IF EXISTS aihub.v_llm_cost;
DROP MATERIALIZED VIEW IF EXISTS aihub.mv_execution_stats_hourly;
DROP MATERIALIZED VIEW IF EXISTS aihub.mv_token_usage_hourly;

COMMIT;
