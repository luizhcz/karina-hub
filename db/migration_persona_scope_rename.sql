-- =============================================================================
-- EfsAiHub — Migration: rename scope 'global' → 'global:cliente'+'global:admin'
--
-- Contexto: o scope de persona_prompt_templates mudou de um valor único
-- 'global' para um par 'global:{userType}'. Essa migration faz o delete
-- da linha antiga uma única vez. O seed (seed_persona_prompt_templates.sql)
-- foi tornado idempotente e NÃO apaga mais nada — basta rodá-lo depois.
--
-- Ordem de execução em ambientes existentes:
--   1. Rodar esta migration UMA VEZ (apaga o scope legado 'global').
--   2. Rodar seed_persona_prompt_templates.sql (insere os dois scopes novos).
--
-- Ambientes novos (primeira instalação): PULAR esta migration — o schema
-- já nasce sem nenhuma linha legada, seed basta.
-- =============================================================================

SET search_path TO aihub;

DELETE FROM aihub.persona_prompt_templates WHERE "Scope" = 'global';

-- Conferência:
--   SELECT "Scope" FROM aihub.persona_prompt_templates;
