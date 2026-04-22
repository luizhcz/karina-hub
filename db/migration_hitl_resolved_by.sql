-- Migration: adiciona coluna ResolvedBy em aihub.human_interactions para auditoria HITL.
-- Executar via psql em bases existentes. Idempotente: ADD COLUMN IF NOT EXISTS.
-- psql -f db/migration_hitl_resolved_by.sql

-- Coluna ResolvedBy — UserId de quem resolveu a interação.
-- Valores esperados:
--   - x-efs-account OU x-efs-user-profile-id do header da request
--   - 'system:timeout' quando o timeout HITL do service expira
--   - NULL para registros pré-migration / Status=Pending
ALTER TABLE aihub.human_interactions
    ADD COLUMN IF NOT EXISTS "ResolvedBy" VARCHAR(128) NULL;

-- Índice covering para queries "quais interações X resolveu em Y período".
-- Partial index (WHERE ResolvedBy IS NOT NULL) — zero overhead em rows pending.
-- Executar CONCURRENTLY em produção para não bloquear writes:
--   psql -c "CREATE INDEX CONCURRENTLY ..."
-- Em dev/staging o CREATE INDEX normal já está idempotente via IF NOT EXISTS.
CREATE INDEX IF NOT EXISTS "IX_human_interactions_ResolvedBy_ResolvedAt"
    ON aihub.human_interactions ("ResolvedBy", "ResolvedAt" DESC)
    WHERE "ResolvedBy" IS NOT NULL;
