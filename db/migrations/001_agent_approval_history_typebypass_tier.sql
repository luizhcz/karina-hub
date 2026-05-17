-- =============================================================================
-- 001_agent_approval_history_typebypass_tier.sql
--
-- Amplia o CHECK do tier do agent_approval_history pra aceitar 'TypeBypass'
-- (novo valor introduzido com a regra de produto "Router publica direto").
-- Aplicar manualmente em prod/homolog após o deploy do código —
-- db/apply.sh não percorre migrations automaticamente.
--
-- Idempotente: DROP IF EXISTS + recreate.
-- =============================================================================

ALTER TABLE aihub.agent_approval_history
    DROP CONSTRAINT IF EXISTS "CK_agent_approval_history_Tier";

ALTER TABLE aihub.agent_approval_history
    ADD CONSTRAINT "CK_agent_approval_history_Tier"
    CHECK ("Tier" IS NULL OR "Tier" IN ('Cosmetic', 'Behavioral', 'TypeBypass'));
