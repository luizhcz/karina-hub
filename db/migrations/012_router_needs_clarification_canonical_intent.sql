-- =============================================================================
-- 012_router_needs_clarification_canonical_intent.sql
--
-- Introduz a 2ª intent reservada do sistema (`needs_clarification`) e expande
-- o schema canônico do Router pra incluir `candidate_intents` no output e
-- `clarification_depth` no operationalMemory.
--
-- Mudanças:
--   (1) Seed da intent `needs_clarification` (IsSystem=true) por tenant —
--       mesma estratégia da migration 004 pra `out_of_scope`. Id canônico
--       `sys-nc-{tenantId}` pra fácil rastreio.
--   (2) Auto-link em todos os Routers existentes (aihub.agent_router_intents).
--   (3) Sobrescreve StructuredOutput.Schema dos Routers existentes pra incluir
--       `candidate_intents` no top-level (array de {intent, confidence}).
--   (4) Sobrescreve OperationalMemory.Schema pra incluir `clarification_depth`
--       (integer, parte do required).
--
-- A coluna `agent_definitions.Data` é TEXT — operações via jsonb_set fazem
-- cast ::jsonb e voltam pra ::text. Idempotente: re-rodar produz mesmo estado.
--
-- Pré-prod: pode reaplicar à vontade. Pós-deploy do código novo, os agentes
-- vão emitir o shape novo via OutputSchemaRenderer; LLMs cobertos pelo prompt
-- do PR 2 saberão preencher `candidate_intents`/`clarification_depth`.
-- =============================================================================

BEGIN;

-- ─── (1) Seed system intent "needs_clarification" por tenant ─────────────────
INSERT INTO aihub.router_intents
    ("Id","TenantId","ProjectId","Name","DisplayName","Description","Examples","IsSystem","CreatedAt","UpdatedAt")
SELECT
    'sys-nc-' || p.tenant_id AS "Id",
    p.tenant_id AS "TenantId",
    'default' AS "ProjectId",
    'needs_clarification' AS "Name",
    'Precisa esclarecer' AS "DisplayName",
    'Mensagem ambígua semanticamente válida pro produto mas que casa com ≥2 intents de negócio com confidence similar. O Router preenche candidate_intents; o nó downstream gera pergunta de desambiguação.' AS "Description",
    '["investir","transferir","ajuda","comprar","consultar"]'::jsonb AS "Examples",
    TRUE AS "IsSystem",
    NOW() AS "CreatedAt",
    NOW() AS "UpdatedAt"
FROM (SELECT DISTINCT tenant_id FROM aihub.projects) p
ON CONFLICT ("TenantId","Name") DO UPDATE
    SET "Description" = EXCLUDED."Description",
        "Examples"    = EXCLUDED."Examples",
        "IsSystem"    = TRUE,
        "UpdatedAt"   = NOW();

-- ─── (2) Auto-link em todos os Routers existentes ────────────────────────────
INSERT INTO aihub.agent_router_intents ("AgentId","IntentId","ProjectId","TenantId","CreatedAt")
SELECT
    a."Id"        AS "AgentId",
    r."Id"        AS "IntentId",
    a."ProjectId" AS "ProjectId",
    a."TenantId"  AS "TenantId",
    NOW()         AS "CreatedAt"
FROM aihub.agent_definitions a
JOIN aihub.router_intents r
    ON r."TenantId" = a."TenantId"
   AND r."Name" = 'needs_clarification'
WHERE (a."Data"::jsonb->>'Type') = 'Router'
ON CONFLICT DO NOTHING;

-- ─── (3) StructuredOutput.Schema: adiciona candidate_intents + clarification_depth
-- Sobrescreve o schema inteiro pro canônico novo. Drift entre Routers
-- customizados e o canônico é aceitável pré-prod (admin pode salvar de novo
-- e o template não wrappa porque marker {intent, operationalMemory} continua
-- presente).
UPDATE aihub.agent_definitions
SET "Data" = (
        jsonb_set(
            jsonb_set(
                "Data"::jsonb,
                '{StructuredOutput}',
                $so${"ResponseFormat":"json_schema","SchemaName":"RouterOutput","SchemaDescription":"Output canônico do Router (intent + confidence + reason + candidate_intents + operationalMemory)","Schema":{"type":"object","additionalProperties":false,"properties":{"intent":{"type":"string","description":"Categoria escolhida do enum de intents resolvido em runtime."},"confidence":{"type":"number","minimum":0,"maximum":1,"description":"Confiança da classificação (0..1)."},"reason":{"type":"string","description":"Justificativa curta da escolha (uso interno de auditoria/debug)."},"candidate_intents":{"type":"array","description":"Intents candidatas quando intent='needs_clarification' (mín. 2 itens). Array vazio nos demais casos.","items":{"type":"object","additionalProperties":false,"properties":{"intent":{"type":"string"},"confidence":{"type":"number","minimum":0,"maximum":1}},"required":["intent","confidence"]}},"operationalMemory":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string"},"last_reason":{"type":"string"},"clarification_depth":{"type":"integer","minimum":0,"description":"Turnos consecutivos com needs_clarification. 0 quando intent != needs_clarification. Em >=2 o Router cai em out_of_scope."}},"required":["last_intent","last_reason","clarification_depth"]}},"required":["intent","confidence","reason","candidate_intents","operationalMemory"]}}$so$::jsonb
            ),
            '{OperationalMemory}',
            $om${"Schema":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string","description":"Nome da última intent classificada pelo Router."},"last_reason":{"type":"string","description":"Razão curta (≤200 chars) da última classificação."},"clarification_depth":{"type":"integer","minimum":0,"description":"Turnos consecutivos com intent='needs_clarification'. Resetado a 0 quando o Router escolhe qualquer outra intent."}},"required":["last_intent","last_reason","clarification_depth"]},"MaxBytes":2048}$om$::jsonb
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE ("Data"::jsonb->>'Type') = 'Router';

COMMIT;
