-- =============================================================================
-- EfsAiHub — Model Pricing Seed
-- PostgreSQL 16+ · Schema: aihub
--
-- Popula aihub.model_pricing com valores oficiais da OpenAI e Azure OpenAI
-- para os modelos gpt-5.2, gpt-5.3 e família gpt-5.4 (full, mini, nano).
--
-- Executar APÓS schema.sql. Pode rodar antes ou depois de model_catalog_seed.sql.
-- Uso:
--   psql -U <usuario> -d <banco> -f seed_model_pricing.sql
--
-- Idempotente: cada linha usa WHERE NOT EXISTS verificando a combinação
-- (ModelId, Provider, EffectiveFrom). Rodar 2x não cria duplicata. Para
-- atualizar preço de um modelo existente, insira uma NOVA linha com
-- EffectiveFrom mais recente — o runtime deve preferir a entrada mais recente.
--
-- Unidade dos valores
--   PricePerInputToken e PricePerOutputToken são armazenados em USD POR TOKEN
--   (não por 1K/1M). Referências oficiais publicam por 1M — a conversão foi
--   feita aqui (1M tokens → 1 token = valor / 1_000_000).
--
-- Fontes consultadas (2026-04-22):
--   - developers.openai.com/api/docs/pricing
--   - platform.openai.com/docs/models/gpt-5.2
--   - learn.microsoft.com/en-us/answers/questions/5841927
--   - azure.microsoft.com/en-us/pricing/details/azure-openai/
--
-- Modelos NÃO incluídos (sem preço oficial publicado):
--   - gpt-5.2-mini, gpt-5.3-mini: variantes não listadas nas páginas
--     oficiais de pricing da OpenAI nem da Microsoft. Se a OpenAI publicar
--     no futuro, adicione novas linhas aqui com EffectiveFrom apropriado.
--
-- Paridade OpenAI <-> Azure OpenAI: confirmada para pay-as-you-go. Se usar
-- data-residency com uplift regional de Azure, ajustar preços manualmente.
-- =============================================================================

SET search_path TO aihub;

-- Timestamp único para todas as linhas deste seed; permite agrupar/remover por data efetiva.
-- Qualquer re-execução com preços diferentes deve usar novo EffectiveFrom.
DO $$
DECLARE
    v_effective_from TIMESTAMPTZ := '2026-04-22T00:00:00Z'::timestamptz;
BEGIN

-- =============================================================================
-- OpenAI (direct) — preços por 1M tokens convertidos para por 1 token
-- =============================================================================

-- gpt-5.2 — Input $1.75 / Output $14.00 por 1M
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.2', 'OPENAI', 0.0000017500, 0.0000140000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.2' AND "Provider" = 'OPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.3 — Input $1.75 / Output $14.00 por 1M (paridade com 5.2 segundo OpenRouter)
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.3', 'OPENAI', 0.0000017500, 0.0000140000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.3' AND "Provider" = 'OPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4 — Input $2.50 / Output $15.00 por 1M
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4', 'OPENAI', 0.0000025000, 0.0000150000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4' AND "Provider" = 'OPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4-mini — Input $0.75 / Output $4.50 por 1M
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4-mini', 'OPENAI', 0.0000007500, 0.0000045000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4-mini' AND "Provider" = 'OPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4-nano — Input $0.20 / Output $1.25 por 1M
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4-nano', 'OPENAI', 0.0000002000, 0.0000012500, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4-nano' AND "Provider" = 'OPENAI' AND "EffectiveFrom" = v_effective_from
);

-- =============================================================================
-- Azure OpenAI — paridade pay-as-you-go com OpenAI direct
-- =============================================================================

-- gpt-5.2 — idem OpenAI
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.2', 'AZUREOPENAI', 0.0000017500, 0.0000140000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.2' AND "Provider" = 'AZUREOPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.3 — idem OpenAI
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.3', 'AZUREOPENAI', 0.0000017500, 0.0000140000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.3' AND "Provider" = 'AZUREOPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4 — idem OpenAI
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4', 'AZUREOPENAI', 0.0000025000, 0.0000150000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4' AND "Provider" = 'AZUREOPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4-mini — idem OpenAI
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4-mini', 'AZUREOPENAI', 0.0000007500, 0.0000045000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4-mini' AND "Provider" = 'AZUREOPENAI' AND "EffectiveFrom" = v_effective_from
);

-- gpt-5.4-nano — idem OpenAI
INSERT INTO aihub.model_pricing ("ModelId", "Provider", "PricePerInputToken", "PricePerOutputToken", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'gpt-5.4-nano', 'AZUREOPENAI', 0.0000002000, 0.0000012500, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.model_pricing
    WHERE "ModelId" = 'gpt-5.4-nano' AND "Provider" = 'AZUREOPENAI' AND "EffectiveFrom" = v_effective_from
);

END $$;

-- =============================================================================
-- FIM DO SEED — verificação
-- =============================================================================

-- Para conferir o resultado:
--   SELECT "ModelId", "Provider", "PricePerInputToken" * 1000000 AS input_per_1M,
--          "PricePerOutputToken" * 1000000 AS output_per_1M, "EffectiveFrom"
--   FROM aihub.model_pricing
--   WHERE "EffectiveTo" IS NULL
--   ORDER BY "Provider", "ModelId";
