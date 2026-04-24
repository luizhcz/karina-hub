-- =============================================================================
-- Migration: A/B testing de templates de persona (F6)
--
-- Contexto: F5 entregou versionamento append-only de templates. F6 permite
-- rodar dois VersionIds em paralelo (A/B) com split de tráfego configurável
-- e binding de outcome via llm_token_usage.
--
-- Decisões (ADR 005 — persona-ab-testing):
--   * Scope do experiment é o mesmo string de template (project:{pid}:*,
--     agent:{aid}:*, global:*). UNIQUE parcial (onde EndedAt IS NULL) garante
--     no máximo 1 experiment ativo por (ProjectId, Scope).
--   * ProjectId é o boundary de isolamento (ADR 003), não TenantId.
--   * VariantAVersionId/VariantBVersionId apontam pra VersionId (UUID) em
--     persona_prompt_template_versions — snapshot imutável. Rollback do
--     template pai não afeta experiment em curso.
--   * TrafficSplitB é % de tráfego pra B (0-100). A = 100 - B.
--   * Metric é semantic hint pra UI agregar (cost_usd, total_tokens,
--     hitl_approved). Backend não enforce — só armazena.
--   * Bucketing: SHA256(userId + experimentId) % 100 < TrafficSplitB → B.
--     Determinístico por userId (sticky across retries).
--
-- Idempotente: IF NOT EXISTS em tabela e índices.
-- =============================================================================

SET search_path TO aihub;

CREATE TABLE IF NOT EXISTS aihub.persona_prompt_experiments (
    "Id"                 SERIAL PRIMARY KEY,
    "ProjectId"          VARCHAR(128) NOT NULL,
    "Scope"              VARCHAR(128) NOT NULL,
    "Name"               VARCHAR(128) NOT NULL,
    "VariantAVersionId"  UUID NOT NULL,
    "VariantBVersionId"  UUID NOT NULL,
    "TrafficSplitB"      INT NOT NULL CHECK ("TrafficSplitB" BETWEEN 0 AND 100),
    "Metric"             VARCHAR(64) NOT NULL,
    "StartedAt"          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "EndedAt"            TIMESTAMPTZ NULL,
    "CreatedBy"          VARCHAR(128) NULL
);

-- Um experiment ativo por (ProjectId, Scope). Past experiments (EndedAt set)
-- não contam pra uniqueness, então um scope pode ter histórico de experiments.
CREATE UNIQUE INDEX IF NOT EXISTS ux_persona_experiments_active
    ON aihub.persona_prompt_experiments("ProjectId", "Scope")
    WHERE "EndedAt" IS NULL;

-- Lookup por project pra enumerar em UI admin.
CREATE INDEX IF NOT EXISTS ix_persona_experiments_project
    ON aihub.persona_prompt_experiments("ProjectId", "StartedAt" DESC);
