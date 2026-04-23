-- =============================================================================
-- Migration: persona_prompt_template_versions (F5)
--
-- Histórico append-only de edições de templates de persona. Cada UPDATE via
-- PUT /admin/persona-templates/{id} grava uma nova version e aponta
-- ActiveVersionId pra ela; rollback cria uma 4ª version copiando o conteúdo
-- da version alvo (audit trail imutável, não pula ponteiro).
--
-- Pattern de inspiração: `agent_prompt_versions` (flat, IsActive flag). Aqui
-- ActiveVersionId fica no template pra evitar escrever em 2 tabelas no
-- critical path.
-- =============================================================================

SET search_path TO aihub;

CREATE TABLE IF NOT EXISTS aihub.persona_prompt_template_versions (
    "Id"             SERIAL PRIMARY KEY,
    "TemplateId"     INT NOT NULL REFERENCES aihub.persona_prompt_templates("Id") ON DELETE CASCADE,
    "VersionId"      UUID NOT NULL DEFAULT gen_random_uuid(),
    "Template"       TEXT NOT NULL,
    "CreatedAt"      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    "CreatedBy"      VARCHAR(128) NULL,
    "ChangeReason"   VARCHAR(512) NULL,
    CONSTRAINT template_length_version CHECK (LENGTH("Template") <= 50000)
);

CREATE INDEX IF NOT EXISTS ix_ptv_template_created
    ON aihub.persona_prompt_template_versions("TemplateId", "CreatedAt" DESC);

CREATE UNIQUE INDEX IF NOT EXISTS ux_ptv_version_id
    ON aihub.persona_prompt_template_versions("VersionId");

-- ActiveVersionId fica no template — aponta pro VersionId ativo.
-- Nullable pra compat com rows pré-F5 (templates sem histórico inicial; a
-- migration de seed abaixo popula).
ALTER TABLE aihub.persona_prompt_templates
    ADD COLUMN IF NOT EXISTS "ActiveVersionId" UUID NULL;

-- Backfill: pra cada template existente, criar uma version inicial espelhando
-- o estado atual e apontar ActiveVersionId. Idempotente via WHERE NOT EXISTS.
INSERT INTO aihub.persona_prompt_template_versions ("TemplateId", "Template", "CreatedAt", "CreatedBy", "ChangeReason")
SELECT t."Id", t."Template", t."CreatedAt", 'migration', 'initial'
FROM aihub.persona_prompt_templates t
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.persona_prompt_template_versions v WHERE v."TemplateId" = t."Id"
);

-- Backfill aponta pra version MAIS RECENTE (MAX Id). Defensivo: se algum
-- outro path popular versions antes da migration rodar, ActiveVersionId
-- deve refletir o estado atual, não o mais antigo.
UPDATE aihub.persona_prompt_templates t
SET "ActiveVersionId" = v."VersionId"
FROM aihub.persona_prompt_template_versions v
WHERE v."TemplateId" = t."Id"
  AND t."ActiveVersionId" IS NULL
  AND v."Id" = (
      SELECT MAX("Id") FROM aihub.persona_prompt_template_versions v2
      WHERE v2."TemplateId" = t."Id"
  );

-- Conferência:
--   SELECT t."Id", t."Scope", t."ActiveVersionId",
--          (SELECT COUNT(*) FROM aihub.persona_prompt_template_versions v WHERE v."TemplateId" = t."Id") AS versions
--   FROM aihub.persona_prompt_templates t;
