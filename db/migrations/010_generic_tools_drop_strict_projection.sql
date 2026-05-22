-- =============================================================================
-- 010_generic_tools_drop_strict_projection.sql
--
-- Simplifica OutputProjectionMode: remove o valor 'Strict' (campos extras
-- não declarados são raros em APIs reais — Strict gerava falsos positivos
-- demais). Tools que estavam em Strict viram Project (drop silencioso de
-- extras + fail-loud em required/type).
--
-- Aplicar manualmente em homolog/prod após o deploy do código.
-- Idempotente.
-- =============================================================================

UPDATE aihub.generic_tools
   SET "OutputProjectionMode" = 'Project'
 WHERE "OutputProjectionMode" = 'Strict';

ALTER TABLE aihub.generic_tools
    DROP CONSTRAINT IF EXISTS "CK_generic_tools_OutputProjectionMode";

ALTER TABLE aihub.generic_tools
    ADD CONSTRAINT "CK_generic_tools_OutputProjectionMode"
    CHECK ("OutputProjectionMode" IN ('Off', 'Project'));
