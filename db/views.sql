-- =============================================================================
-- EfsAiHub — Views
-- PostgreSQL 16+ · Schema: aihub
--
-- Executar APÓS schema.sql.
-- Uso:
--   psql -U <usuario> -d <banco> -f views.sql
--
-- Histórico: até a migration 016 havia 3 materialized views de analytics
-- (v_llm_cost, mv_execution_stats_hourly, mv_token_usage_hourly) com refresh
-- agendado pelo LlmCostRefreshService. Foram removidas — REFRESH MATERIALIZED
-- VIEW exige privilege especial que nem todo deploy tem. O cálculo de custo
-- LLM (LATERAL JOIN com model_pricing) agora roda em CTE inline no
-- PgProjectAnalyticsRepository, com cache Redis (TTL 30min, invalidação via
-- POST /api/aihub/analytics/projects/{id}/refresh).
-- =============================================================================

SET search_path TO aihub;

-- =============================================================================
-- v_production_executions — execuções de workflows reais (sem sandbox)
-- =============================================================================
-- Dashboards/analytics consomem essa view ao invés de workflow_executions
-- direto. Filtra workflows efêmeros criados por sandbox sessions (Chat ou
-- Standalone) pra que a UI de produção não inflame com gastos de teste.
--
-- Compliance preservado: llm_token_usage continua com TODAS as rows (sandbox
-- + predict-intent inclusive) pra resposta a audit/finance. Esta view só
-- esconde de quem olha dashboard de prod — dado bruto fica intacto na
-- tabela base.
--
-- Filtro por prefixo do WorkflowId é resiliente a cleanup do workflow_definition
-- (que pode ser deletado por TTL antes da execution; joining com
-- workflow_definitions daria NULL pós-cleanup e exigiria COALESCE — prefixo
-- no WorkflowId é estável e barato).
--
-- Predict-intent não cria workflow_execution: rows de llm_token_usage com
-- WorkflowId='router-predict:*' já são naturalmente filtradas em queries
-- de analytics porque o INNER JOIN com workflow_executions não bate.
-- =============================================================================
CREATE OR REPLACE VIEW aihub.v_production_executions AS
SELECT *
FROM aihub.workflow_executions
WHERE "WorkflowId" NOT LIKE 'deploy-chat-sandbox-%'
  AND "WorkflowId" NOT LIKE 'sandbox-standalone-%';

-- =============================================================================
-- FIM DAS VIEWS
-- =============================================================================
