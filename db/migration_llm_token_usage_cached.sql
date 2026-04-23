-- =============================================================================
-- Migration: llm_token_usage.CachedTokens
--
-- Adiciona coluna pra capturar cached_tokens do OpenAI prompt caching
-- (via UsageDetails.CachedInputTokenCount do Microsoft.Extensions.AI 10.5.0).
-- Não-destrutivo: DEFAULT 0 cobre rows existentes; writers antigos continuam
-- funcionando (coluna opcional no INSERT).
--
-- Ver ADR: docs/adr/000-opensdk-shape.md
-- =============================================================================

SET search_path TO aihub;

ALTER TABLE aihub.llm_token_usage
    ADD COLUMN IF NOT EXISTS "CachedTokens" INT NOT NULL DEFAULT 0;

-- Conferência:
--   SELECT COUNT(*) FROM aihub.llm_token_usage WHERE "CachedTokens" > 0;
