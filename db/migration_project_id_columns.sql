-- =============================================================================
-- Migration: ProjectId columns em node_executions e llm_token_usage
--
-- F4 (revisão pós-ADR 003): escolhemos ProjectId como boundary de isolamento
-- em vez de TenantId paralelo. Razão: `projects.tenant_id` já define a
-- hierarquia tenant → project, e 6 entidades já são filtradas por ProjectId
-- via HasQueryFilter. Adicionar TenantId paralelo seria defense-in-depth
-- redundante por ora.
--
-- Conversations JÁ TEM ProjectId (schema.sql). Node_executions e
-- llm_token_usage não tinham — esta migration adiciona.
--
-- Limpeza: se uma migration anterior adicionou coluna "TenantId" nessas
-- tabelas (F4 pivot abortado), ela é removida aqui também.
-- =============================================================================

SET search_path TO aihub;

-- node_executions — ADD ProjectId + DROP TenantId (pivot cleanup)
ALTER TABLE aihub.node_executions
    ADD COLUMN IF NOT EXISTS "ProjectId" VARCHAR(128) NULL;
ALTER TABLE aihub.node_executions
    DROP COLUMN IF EXISTS "TenantId";
DROP INDEX IF EXISTS aihub.ix_node_executions_tenant_exec;
CREATE INDEX IF NOT EXISTS ix_node_executions_project_exec
    ON aihub.node_executions ("ProjectId", "ExecutionId");

-- llm_token_usage — ADD ProjectId + DROP TenantId (pivot cleanup)
ALTER TABLE aihub.llm_token_usage
    ADD COLUMN IF NOT EXISTS "ProjectId" VARCHAR(128) NULL;
ALTER TABLE aihub.llm_token_usage
    DROP COLUMN IF EXISTS "TenantId";
DROP INDEX IF EXISTS aihub.ix_llm_token_usage_tenant_created;
CREATE INDEX IF NOT EXISTS ix_llm_token_usage_project_created
    ON aihub.llm_token_usage ("ProjectId", "CreatedAt" DESC);

-- conversations: coluna TenantId também ficou quando F4 estava em TenantId;
-- limpeza (conversations já tem ProjectId desde schema.sql).
ALTER TABLE aihub.conversations
    DROP COLUMN IF EXISTS "TenantId";
DROP INDEX IF EXISTS aihub.ix_conversations_tenant_created;

-- Conferência:
--   SELECT table_name, column_name FROM information_schema.columns
--   WHERE table_schema='aihub' AND column_name='ProjectId' ORDER BY 1;
