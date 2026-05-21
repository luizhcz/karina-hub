-- =============================================================================
-- 008_agent_approval_history_propagated_dependency_tier.sql
--
-- Amplia o CHECK do tier do agent_approval_history pra aceitar
-- 'PropagatedDependency' (auto-snapshot disparado quando uma dep — RouterIntent,
-- GenericTool, PredefinedModel ou master prompt — é editada e o propagator
-- recompõe os agentes referenciadores).
--
-- Aplicar manualmente em prod/homolog após o deploy do código.
-- Idempotente: DROP IF EXISTS + recreate.
-- =============================================================================

ALTER TABLE aihub.agent_approval_history
    DROP CONSTRAINT IF EXISTS "CK_agent_approval_history_Tier";

ALTER TABLE aihub.agent_approval_history
    ADD CONSTRAINT "CK_agent_approval_history_Tier"
    CHECK ("Tier" IS NULL OR "Tier" IN ('Cosmetic', 'Behavioral', 'TypeBypass', 'PropagatedDependency'));
