-- =============================================================================
-- Migration: backfill ProjectId em rows legadas (F5.5)
--
-- Contexto: F4 adicionou coluna ProjectId em node_executions e llm_token_usage
-- com DEFAULT NULL. O HasQueryFilter foi escrito tolerante a null pra não
-- quebrar rows pré-F4, mas isso abre brecha — analytics globais podem
-- exibir rows null agregadas como "projeto desconhecido" e um caller que
-- omite projectId escreve em null visível cross-project.
--
-- Esta migration faz backfill das rows legadas pra 'default' (tenant default
-- do seed). A remoção da cláusula `|| e.ProjectId == null` fica em PR
-- separado (ver TENANCY-STRICT-FILTER no backlog), depois do warning log
-- em WorkflowRunnerService parar de aparecer.
--
-- Idempotente: se rodar 2x, a 2ª não faz nada (WHERE ... IS NULL).
-- =============================================================================

SET search_path TO aihub;

UPDATE aihub.node_executions
SET "ProjectId" = 'default'
WHERE "ProjectId" IS NULL;

UPDATE aihub.llm_token_usage
SET "ProjectId" = 'default'
WHERE "ProjectId" IS NULL;

-- Conferência:
--   SELECT COUNT(*) FROM aihub.node_executions WHERE "ProjectId" IS NULL;
--   SELECT COUNT(*) FROM aihub.llm_token_usage WHERE "ProjectId" IS NULL;
-- Esperado: 0 em ambos após primeiro run.
