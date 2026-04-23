-- =============================================================================
-- Migration: persona_prompt_templates.Template length constraint
--
-- Antes: coluna TEXT sem CHECK — admin poderia salvar 10MB via curl e
-- inflar o cache/prompt até quebrar o tenant. 50.000 chars ≈ 10k tokens
-- (proxy ~4 chars/token), suficiente para templates ricos.
--
-- Não-destrutivo se nenhuma row existente exceder 50000. Confere via:
--   SELECT COUNT(*) FROM aihub.persona_prompt_templates WHERE LENGTH("Template") > 50000;
-- =============================================================================

SET search_path TO aihub;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conname = 'template_length'
          AND conrelid = 'aihub.persona_prompt_templates'::regclass
    ) THEN
        ALTER TABLE aihub.persona_prompt_templates
            ADD CONSTRAINT template_length CHECK (LENGTH("Template") <= 50000);
    END IF;
END
$$;
