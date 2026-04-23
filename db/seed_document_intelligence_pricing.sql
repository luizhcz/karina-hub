-- =============================================================================
-- EfsAiHub — Document Intelligence Pricing Seed
-- PostgreSQL 16+ · Schema: aihub
--
-- Popula aihub.document_intelligence_pricing com valores oficiais do Azure AI
-- Document Intelligence (antes conhecido como Form Recognizer). Unidade de
-- cobrança é por PÁGINA, diferente de LLM que é por token.
--
-- Executar APÓS schema.sql.
-- Uso:
--   psql -U <usuario> -d <banco> -f seed_document_intelligence_pricing.sql
--
-- Idempotente: WHERE NOT EXISTS verifica (ModelId, Provider, EffectiveFrom).
-- Para atualizar preço: insira NOVA linha com EffectiveFrom mais recente — o
-- runtime lê a linha com maior EffectiveFrom que esteja vigente (EffectiveTo
-- NULL ou > NOW()).
--
-- Unidade dos valores
--   PricePerPage é armazenado em USD por 1 página. Documentação oficial lista
--   em USD por 1000 páginas — a conversão está feita aqui ($10/1000 = $0.01/pág).
--
-- Add-ons NÃO incluídos (decisão consciente)
--   - High-resolution OCR, fontes, fórmulas: +$6.00/1000 pág
--   - Query Fields: +$10.00/1000 pág
--   Se algum workflow precisar, modelar como features extras (backlog DI-1).
--
-- Commitment tier NÃO modelado (backlog DI-2)
--   Se volume mensal ultrapassar ~15k páginas, commitment reduz ~20% o custo.
--
-- Fontes oficiais consultadas (2026-04-22):
--   - azure.microsoft.com/en-us/pricing/details/document-intelligence/
--   - learn.microsoft.com/en-us/azure/ai-services/document-intelligence/
--   - learn.microsoft.com/en-us/answers/questions/5592258
--
-- Paridade US East / Brazil South: cobrança em USD pela tarifa US base; IOF
-- aplicável no Brazil South mas sem diferença tarifária estrutural.
-- =============================================================================

SET search_path TO aihub;

DO $$
DECLARE
    v_effective_from TIMESTAMPTZ := '2026-04-22T00:00:00Z'::timestamptz;
BEGIN

-- =============================================================================
-- Azure AI Document Intelligence — Pay-as-you-go Standard (S0)
-- =============================================================================

-- prebuilt-layout — análise de layout com tabelas + markdown output (FOCO DO PROJETO)
-- $10.00 por 1000 páginas
INSERT INTO aihub.document_intelligence_pricing
    ("ModelId", "Provider", "PricePerPage", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'prebuilt-layout', 'AZUREAI', 0.0100000000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.document_intelligence_pricing
    WHERE "ModelId" = 'prebuilt-layout' AND "Provider" = 'AZUREAI' AND "EffectiveFrom" = v_effective_from
);

-- prebuilt-read — OCR básico (mais barato, sem estrutura)
-- $1.50 por 1000 páginas (drop para $0.60 acima de 1M/mês, não modelado aqui)
INSERT INTO aihub.document_intelligence_pricing
    ("ModelId", "Provider", "PricePerPage", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'prebuilt-read', 'AZUREAI', 0.0015000000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.document_intelligence_pricing
    WHERE "ModelId" = 'prebuilt-read' AND "Provider" = 'AZUREAI' AND "EffectiveFrom" = v_effective_from
);

-- prebuilt-invoice — $10.00 por 1000 páginas
INSERT INTO aihub.document_intelligence_pricing
    ("ModelId", "Provider", "PricePerPage", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'prebuilt-invoice', 'AZUREAI', 0.0100000000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.document_intelligence_pricing
    WHERE "ModelId" = 'prebuilt-invoice' AND "Provider" = 'AZUREAI' AND "EffectiveFrom" = v_effective_from
);

-- prebuilt-receipt — $10.00 por 1000 páginas
INSERT INTO aihub.document_intelligence_pricing
    ("ModelId", "Provider", "PricePerPage", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'prebuilt-receipt', 'AZUREAI', 0.0100000000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.document_intelligence_pricing
    WHERE "ModelId" = 'prebuilt-receipt' AND "Provider" = 'AZUREAI' AND "EffectiveFrom" = v_effective_from
);

-- prebuilt-idDocument — $10.00 por 1000 páginas
INSERT INTO aihub.document_intelligence_pricing
    ("ModelId", "Provider", "PricePerPage", "Currency", "EffectiveFrom", "CreatedAt")
SELECT 'prebuilt-idDocument', 'AZUREAI', 0.0100000000, 'USD', v_effective_from, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM aihub.document_intelligence_pricing
    WHERE "ModelId" = 'prebuilt-idDocument' AND "Provider" = 'AZUREAI' AND "EffectiveFrom" = v_effective_from
);

END $$;

-- =============================================================================
-- FIM DO SEED — verificação
-- =============================================================================

-- Para conferir o resultado:
--   SELECT "ModelId", "Provider",
--          ("PricePerPage" * 1000)::numeric(10,4) AS usd_per_1000_pages,
--          "EffectiveFrom"::date
--   FROM aihub.document_intelligence_pricing
--   WHERE "EffectiveTo" IS NULL
--   ORDER BY "Provider", "ModelId";
