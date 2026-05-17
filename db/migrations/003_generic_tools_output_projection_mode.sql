-- =============================================================================
-- 003_generic_tools_output_projection_mode.sql
--
-- Adiciona coluna OutputProjectionMode em aihub.generic_tools pra controlar
-- como o response da tool é validado e projetado contra OutputSchema antes
-- de chegar ao LLM/tester. Default 'Off' preserva tools existentes
-- (comportamento legacy) — admin opta in via ToolEditor.
--
-- Valores:
--   'Off'     = bypass total (parser devolve response cru).
--   'Project' = drop silencioso de extras + fail-loud em required/type.
--   'Strict'  = Project + fail-loud em campos extras.
--
-- Idempotente: ADD COLUMN IF NOT EXISTS + CHECK gateado por
-- information_schema lookup.
-- =============================================================================

ALTER TABLE aihub.generic_tools
    ADD COLUMN IF NOT EXISTS "OutputProjectionMode" VARCHAR(16) NOT NULL DEFAULT 'Off';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM information_schema.table_constraints
        WHERE constraint_schema = 'aihub'
          AND table_name = 'generic_tools'
          AND constraint_name = 'CK_generic_tools_OutputProjectionMode'
    ) THEN
        ALTER TABLE aihub.generic_tools
            ADD CONSTRAINT "CK_generic_tools_OutputProjectionMode"
            CHECK ("OutputProjectionMode" IN ('Off', 'Project', 'Strict'));
    END IF;
END $$;
