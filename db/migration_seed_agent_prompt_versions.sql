-- Migration: reconcilia aihub.agent_prompt_versions com aihub.agent_definitions.
-- Idempotente: cria a versão 'v1' ATIVA para cada agente que tem Instructions
-- não vazias mas nenhuma linha em agent_prompt_versions.
--
-- Motivo: agents seedados diretamente via SQL (db/seed_default_project.sql) gravam
-- só em agent_definitions; o SeedInitialPromptAsync do AgentService só roda via
-- POST /api/agents. Resultado: GET /agents/{id}/prompts/active → 404.
--
--   psql -f db/migration_seed_agent_prompt_versions.sql

INSERT INTO aihub.agent_prompt_versions ("AgentId", "VersionId", "Content", "IsActive", "CreatedAt")
SELECT
    d."Id",
    'v1',
    d."Data"::jsonb ->> 'Instructions',
    TRUE,
    COALESCE(d."CreatedAt", NOW())
FROM aihub.agent_definitions d
WHERE COALESCE(TRIM(d."Data"::jsonb ->> 'Instructions'), '') <> ''
  AND NOT EXISTS (
      SELECT 1 FROM aihub.agent_prompt_versions p WHERE p."AgentId" = d."Id"
  );
