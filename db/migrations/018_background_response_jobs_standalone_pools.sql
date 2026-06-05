-- =============================================================================
-- 018_background_response_jobs_standalone_pools.sql
--
-- Schema pra suportar workflows standalone com filas isoladas por workflow,
-- lease/heartbeat pra recovery cross-pod, sub-estado por handler multi-step
-- (populado por pipelines downstream) e ETag no GET de polling.
--
-- Adicionado em ALTER TABLE (não-destrutivo, tabela pré-existente):
--   WorkflowId       — alvo do workflow (hot path da cota por workflow)
--   Step             — sub-estado dentro de Running (handler multi-step)
--   LeasedBy         — podId que pegou o job via FOR UPDATE SKIP LOCKED
--   LeaseUntil       — expiração do lease (reaper reseta jobs com lease estourado)
--   NextAttemptAt    — backoff exponencial entre retries
--   IngestionContext — JSONB com contexto de handlers downstream
--   ExecutionId      — workflow_executions.execution_id disparado pelo job
--   UpdatedAt        — compõe ETag do GET; defaults pra now() pras rows existentes
--   ProjectId        — multi-tenant scope (GET filtra por isso) — default 'default'
--                      pras rows pré-existentes; inserts novos populam via context.
--   TenantId         — mesma justificativa
--
-- Índices novos:
--   (WorkflowId, Status)        — cota por workflow
--   (Status, NextAttemptAt) partial — dispatcher pega Queued elegíveis
--   (LeaseUntil) partial        — reaper varre leases expirados
--   (TenantId, ProjectId, JobId) — GET multi-tenant (lookup com scope)
--
-- Idempotente: ADD COLUMN IF NOT EXISTS + CREATE INDEX IF NOT EXISTS.
-- =============================================================================

BEGIN;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "WorkflowId"       VARCHAR(256) NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "Step"             VARCHAR(32)  NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "LeasedBy"         VARCHAR(64)  NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "LeaseUntil"       TIMESTAMPTZ  NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "NextAttemptAt"    TIMESTAMPTZ  NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "IngestionContext" JSONB        NULL;

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "ExecutionId"      VARCHAR(64)  NULL;

-- UpdatedAt com default explícito; rows pré-existentes recebem now() na migration.
ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "UpdatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW();

-- Multi-tenant scope (default 'default' pra rows pré-existentes; controller popula
-- via IProjectContextAccessor/ITenantContextAccessor em todo INSERT novo).
ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "ProjectId"        VARCHAR(128) NOT NULL DEFAULT 'default';

ALTER TABLE aihub.background_response_jobs
    ADD COLUMN IF NOT EXISTS "TenantId"         VARCHAR(128) NOT NULL DEFAULT 'default';

CREATE INDEX IF NOT EXISTS "IX_background_response_jobs_WorkflowId_Status"
    ON aihub.background_response_jobs ("WorkflowId", "Status")
    WHERE "WorkflowId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_background_response_jobs_Status_NextAttemptAt"
    ON aihub.background_response_jobs ("Status", "NextAttemptAt")
    WHERE "Status" = 'Queued';

CREATE INDEX IF NOT EXISTS "IX_background_response_jobs_LeaseUntil"
    ON aihub.background_response_jobs ("LeaseUntil")
    WHERE "LeaseUntil" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_background_response_jobs_TenantId_ProjectId"
    ON aihub.background_response_jobs ("TenantId", "ProjectId");

COMMIT;
