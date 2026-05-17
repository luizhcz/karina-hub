-- =============================================================================
-- 004_router_canonical_output_and_system_intents.sql
--
-- Migração canônica do Router:
--   (1) Adiciona coluna "IsSystem" em aihub.router_intents (intents reservadas
--       do sistema — protegidas contra delete/edit).
--   (2) Seeda intent "out_of_scope" (IsSystem=true) + intents de negócio
--       "boleta"/"recomendacao" (IsSystem=false) por tenant.
--   (3) Converte agentes router-atendimento-* de Custom → Router +
--       sobrescreve StructuredOutput pro canônico {intent,confidence,reason,
--       operationalMemory} + adiciona OperationalMemory canônica.
--   (4) Linka as 3 intents (out_of_scope + boleta + recomendacao) em todos os
--       Routers via aihub.agent_router_intents (CASCADE/RESTRICT).
--   (5) Atualiza workflows atendimento-{assessor,cliente}: Switch passa a
--       casar em $.intent (canônico) com cases boleta/recomendacao/
--       out_of_scope/default → todos os fallbacks apontam pra
--       fallback-atendimento. Remove executor router_fallback (não existe
--       mais — deletado em ServiceCollectionExtensions).
--
-- IMPORTANTE: agent_definitions.Data e workflow_definitions.Data são TEXT
-- (JSON serializado). Operações com jsonb_set fazem cast ::jsonb e
-- reconvertem com ::text antes do UPDATE.
--
-- Esta migration faz parte do redesign que elimina o legado RouterOutput
-- ({ target_agent, reasoning, message }) e padroniza o output canônico. NÃO
-- precisa ser revertida no rollback do PR (idempotente em todo passo).
-- =============================================================================

BEGIN;

-- ─── (1) Coluna IsSystem na pool ────────────────────────────────────────────
ALTER TABLE aihub.router_intents
    ADD COLUMN IF NOT EXISTS "IsSystem" BOOLEAN NOT NULL DEFAULT FALSE;

-- ─── (2a) Seed system intent "out_of_scope" por tenant ──────────────────────
INSERT INTO aihub.router_intents
    ("Id","TenantId","ProjectId","Name","DisplayName","Description","Examples","IsSystem","CreatedAt","UpdatedAt")
SELECT
    'sys-oos-' || p.tenant_id AS "Id",
    p.tenant_id AS "TenantId",
    'default' AS "ProjectId",
    'out_of_scope' AS "Name",
    'Fora de escopo' AS "DisplayName",
    'Mensagem fora do escopo declarado do Router. Use sempre que nenhuma intent de negócio combinar (saudações, perguntas genéricas, off-topic).' AS "Description",
    '["oi","obrigado","que horas são?","fala sobre política","você é uma IA?"]'::jsonb AS "Examples",
    TRUE AS "IsSystem",
    NOW() AS "CreatedAt",
    NOW() AS "UpdatedAt"
FROM (SELECT DISTINCT tenant_id FROM aihub.projects) p
ON CONFLICT ("TenantId","Name") DO UPDATE
    SET "Description" = EXCLUDED."Description",
        "Examples"    = EXCLUDED."Examples",
        "IsSystem"    = TRUE,
        "UpdatedAt"   = NOW();

-- ─── (2b) Seed intents de negócio "boleta" e "recomendacao" por tenant ──────
INSERT INTO aihub.router_intents
    ("Id","TenantId","ProjectId","Name","DisplayName","Description","Examples","IsSystem","CreatedAt","UpdatedAt")
SELECT
    'biz-' || x.iname || '-' || p.tenant_id AS "Id",
    p.tenant_id AS "TenantId",
    'default' AS "ProjectId",
    x.iname AS "Name",
    x.disp AS "DisplayName",
    x.descr AS "Description",
    x.examples::jsonb AS "Examples",
    FALSE AS "IsSystem",
    NOW() AS "CreatedAt",
    NOW() AS "UpdatedAt"
FROM (SELECT DISTINCT tenant_id FROM aihub.projects) p
CROSS JOIN (VALUES
    ('boleta', 'Boleta',
     'Operações de compra/venda de ativos: ordens, boletas, posições, "vende tudo", "a mercado", "limitado".',
     '["compra 100 PETR4 a 30","vende tudo de VALE3","boleta de venda BBDC4"]'),
    ('recomendacao', 'Recomendação',
     'Consultas sobre recomendações de ativos: upside, target price, preço-alvo, "o que acha de X", "vale a pena".',
     '["qual recomendação de BTLG11?","upside de PETR4","preço alvo de VALE3"]')
) AS x(iname, disp, descr, examples)
ON CONFLICT ("TenantId","Name") DO NOTHING;

-- ─── (3) Custom → Router + StructuredOutput canônica + OperationalMemory ───
-- Data é TEXT — cast pra jsonb, aplica jsonb_set, volta pra text.
UPDATE aihub.agent_definitions
SET "Data" = (
        jsonb_set(
            jsonb_set(
                jsonb_set("Data"::jsonb, '{Type}', '"Router"'),
                '{StructuredOutput}',
                '{"ResponseFormat":"json_schema","SchemaName":"RouterOutput","SchemaDescription":"Output canônico do Router (intent + confidence + reason + operationalMemory)","Schema":{"type":"object","additionalProperties":false,"properties":{"intent":{"type":"string","description":"Categoria escolhida do enum de intents resolvido em runtime."},"confidence":{"type":"number","minimum":0,"maximum":1,"description":"Confiança da classificação (0..1)."},"reason":{"type":"string","description":"Justificativa curta da escolha (uso interno de auditoria/debug)."},"operationalMemory":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string"},"last_reason":{"type":"string"}},"required":["last_intent","last_reason"]}},"required":["intent","confidence","reason","operationalMemory"]}}'::jsonb
            ),
            '{OperationalMemory}',
            '{"Schema":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string","description":"Nome da última intent classificada pelo Router."},"last_reason":{"type":"string","description":"Razão curta (≤200 chars) da última classificação."}},"required":["last_intent","last_reason"]},"MaxBytes":2048}'::jsonb
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE "Id" IN ('router-atendimento-assessor', 'router-atendimento-cliente');

-- ─── (4) Auto-link das 3 intents nos Routers ────────────────────────────────
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
   AND r."Name" IN ('out_of_scope', 'boleta', 'recomendacao')
WHERE (a."Data"::jsonb->>'Type') = 'Router'
ON CONFLICT DO NOTHING;

-- ─── (5a) Atendimento Cliente: Switch passa a usar $.intent ────────────────
UPDATE aihub.workflow_definitions
SET "Data" = (
        jsonb_set(
            jsonb_set(
                jsonb_set(
                    "Data"::jsonb,
                    '{Executors}',
                    (
                        SELECT COALESCE(jsonb_agg(e), '[]'::jsonb)
                        FROM jsonb_array_elements(("Data"::jsonb)->'Executors') e
                        WHERE e->>'Id' <> 'router_fallback'
                    )
                ),
                '{Edges}',
                (
                    SELECT jsonb_agg(
                        CASE
                            WHEN edge->>'From' = 'router-atendimento-cliente' AND edge->>'EdgeType' = 'Switch'
                            THEN jsonb_set(edge, '{Cases}', $cases$[
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"boleta","ValueType":"String"},"Targets":["agente-boleta-cliente"],"IsDefault":false},
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"recomendacao","ValueType":"String"},"Targets":["agente-recomendacao"],"IsDefault":false},
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"out_of_scope","ValueType":"String"},"Targets":["fallback-atendimento"],"IsDefault":false},
                                {"Targets":["fallback-atendimento"],"IsDefault":true}
                            ]$cases$::jsonb)
                            ELSE edge
                        END
                    )
                    FROM jsonb_array_elements(("Data"::jsonb)->'Edges') edge
                )
            ),
            '{Configuration,OutputNodes}',
            '["unwrap_post_processor_output","agente-recomendacao","fallback-atendimento"]'::jsonb
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE "Id" = 'atendimento-cliente';

-- ─── (5b) Atendimento Assessor: análogo, target=agente-boleta-assessor ─────
UPDATE aihub.workflow_definitions
SET "Data" = (
        jsonb_set(
            jsonb_set(
                jsonb_set(
                    "Data"::jsonb,
                    '{Executors}',
                    (
                        SELECT COALESCE(jsonb_agg(e), '[]'::jsonb)
                        FROM jsonb_array_elements(("Data"::jsonb)->'Executors') e
                        WHERE e->>'Id' <> 'router_fallback'
                    )
                ),
                '{Edges}',
                (
                    SELECT jsonb_agg(
                        CASE
                            WHEN edge->>'From' = 'router-atendimento-assessor' AND edge->>'EdgeType' = 'Switch'
                            THEN jsonb_set(edge, '{Cases}', $cases$[
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"boleta","ValueType":"String"},"Targets":["agente-boleta-assessor"],"IsDefault":false},
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"recomendacao","ValueType":"String"},"Targets":["agente-recomendacao"],"IsDefault":false},
                                {"Predicate":{"Path":"$.intent","Operator":"Eq","Value":"out_of_scope","ValueType":"String"},"Targets":["fallback-atendimento"],"IsDefault":false},
                                {"Targets":["fallback-atendimento"],"IsDefault":true}
                            ]$cases$::jsonb)
                            ELSE edge
                        END
                    )
                    FROM jsonb_array_elements(("Data"::jsonb)->'Edges') edge
                )
            ),
            '{Configuration,OutputNodes}',
            '["unwrap_post_processor_output","agente-recomendacao","fallback-atendimento"]'::jsonb
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE "Id" = 'atendimento-assessor';

COMMIT;
