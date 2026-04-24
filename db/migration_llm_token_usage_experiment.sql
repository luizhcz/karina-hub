-- =============================================================================
-- Migration: outcome binding de experiments em llm_token_usage (F6)
--
-- Contexto: F6 introduz A/B testing de templates de persona. Pra poder
-- agregar métricas por variant (cost_usd, total_tokens) precisamos persistir
-- ExperimentId + Variant em cada LLM call executada sob um experiment ativo.
--
-- Decisões:
--   * ExperimentId é FK lógica (sem REFERENCES) pra persona_prompt_experiments:
--     permite experiment ser deletado sem cascatear em analytics históricos.
--   * ExperimentVariant é 'A'/'B' (CHAR(1)). Nullable — a maioria das rows
--     não participam de experiment.
--   * Não adicionamos índice composto por default; queries de aggregate
--     (GET {id}/results) usam WHERE ExperimentId + GROUP BY Variant e o
--     cardinality é baixo. Um índice simples em ExperimentId basta.
--
-- Idempotente: IF NOT EXISTS.
-- =============================================================================

SET search_path TO aihub;

ALTER TABLE aihub.llm_token_usage
    ADD COLUMN IF NOT EXISTS "ExperimentId" INT NULL,
    ADD COLUMN IF NOT EXISTS "ExperimentVariant" CHAR(1) NULL
        CHECK ("ExperimentVariant" IN ('A', 'B'));

CREATE INDEX IF NOT EXISTS ix_llm_token_usage_experiment
    ON aihub.llm_token_usage("ExperimentId")
    WHERE "ExperimentId" IS NOT NULL;
