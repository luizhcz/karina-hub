-- =============================================================================
-- Migration: drop coluna UpdatedBy de persona_prompt_templates (F9)
--
-- *** DO NOT APPLY NA MESMA RELEASE que introduz a remoção do UpdatedBy do
-- código da aplicação. ***
--
-- Contexto: F9 removeu UpdatedBy da entity, DTO, repo e domain. Release
-- atual ignora a coluna; EF não seleciona nem popula. Rows novas têm
-- "UpdatedBy" NULL. Rows antigas mantêm o valor histórico.
--
-- Checklist antes de aplicar (critério de rollback seguro):
--   1. Release com F9 no código está rodando em prod há ≥ 1 deploy cycle.
--   2. Nenhum log/metric indica consumidor externo lendo a coluna.
--   3. Backup recente da tabela (pg_dump -t aihub.persona_prompt_templates).
--   4. Time de DBA/ops aprova janela de manutenção.
--
-- Se qualquer um dos 4 falhar, adiar o DROP. A coluna é cheap de manter NULL.
--
-- =============================================================================

SET search_path TO aihub;

ALTER TABLE aihub.persona_prompt_templates
    DROP COLUMN IF EXISTS "UpdatedBy";

-- Idempotente: DROP COLUMN IF EXISTS passa sem erro se já foi removida.
