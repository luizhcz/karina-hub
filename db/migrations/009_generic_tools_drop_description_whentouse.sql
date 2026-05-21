-- =============================================================================
-- 009_generic_tools_drop_description_whentouse.sql
--
-- Remove as colunas Description e WhenToUse de aihub.generic_tools. A
-- descrição semântica de "o que a tool faz" e "quando usá-la" passa a ser
-- responsabilidade do prompt do agente (parte de perfil/instructions) —
-- a tool fica como instrumento puro: Name + URL + schema.
--
-- Aplicar manualmente em homolog/prod após o deploy do código.
-- Idempotente via IF EXISTS.
-- =============================================================================

ALTER TABLE aihub.generic_tools DROP COLUMN IF EXISTS "Description";
ALTER TABLE aihub.generic_tools DROP COLUMN IF EXISTS "WhenToUse";
