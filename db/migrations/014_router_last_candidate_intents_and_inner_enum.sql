-- =============================================================================
-- 014_router_last_candidate_intents_and_inner_enum.sql
--
-- Expande o canônico do Router em duas frentes:
--
-- (1) `operationalMemory` ganha `last_candidate_intents` (array de strings) —
--     permite ao Router resolver clarification follow-up no turno seguinte
--     ("Renda fixa ou variável?" → "fixa") sem depender de inferência do
--     histórico de chat.
--
-- (2) Schema do StructuredOutput é re-emitido com a mesma forma (espelha
--     last_candidate_intents dentro de operationalMemory). O inner
--     `candidate_intents[].intent` permanece como string aberta no schema
--     persistido — o `OutputSchemaRenderer` injeta o enum runtime com nomes
--     do pool de intents do agente (igual o top-level `intent`).
--
-- Atualiza tanto agent_definitions (Routers existentes) quanto
-- aihub.operational_memory (preenche `last_candidate_intents: []` em rows
-- antigas pra que strict mode do LLM não rejeite o payload injetado).
--
-- Pré-prod: o overwrite do schema canônico segue mesmo critério da 012 (não
-- detecta marker, sobrescreve todos os Routers). Aceito.
-- Idempotente em todos os passos.
-- =============================================================================

BEGIN;

-- ─── (1) Schema canônico atualizado nos Routers ──────────────────────────────
UPDATE aihub.agent_definitions
SET "Data" = (
        jsonb_set(
            jsonb_set(
                "Data"::jsonb,
                '{StructuredOutput}',
                $so${"ResponseFormat":"json_schema","SchemaName":"RouterOutput","SchemaDescription":"Output canônico do Router (intent + confidence + reason + candidate_intents + operationalMemory)","Schema":{"type":"object","additionalProperties":false,"properties":{"intent":{"type":"string","description":"Categoria escolhida do enum de intents resolvido em runtime."},"confidence":{"type":"number","minimum":0,"maximum":1,"description":"Confiança da classificação (0..1)."},"reason":{"type":"string","description":"Justificativa curta da escolha (uso interno de auditoria/debug)."},"candidate_intents":{"type":"array","description":"Intents candidatas quando intent='needs_clarification' (mín. 2 itens). Array vazio nos demais casos.","items":{"type":"object","additionalProperties":false,"properties":{"intent":{"type":"string"},"confidence":{"type":"number","minimum":0,"maximum":1}},"required":["intent","confidence"]}},"operationalMemory":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string"},"last_reason":{"type":"string"},"clarification_depth":{"type":"integer","minimum":0,"description":"Turnos consecutivos com needs_clarification. 0 quando intent != needs_clarification. Em >=2 o Router cai em out_of_scope."},"last_candidate_intents":{"type":"array","description":"Snapshot das candidate_intents do último turno needs_clarification (só nomes). Vazio nos demais casos.","items":{"type":"string"}}},"required":["last_intent","last_reason","clarification_depth","last_candidate_intents"]}},"required":["intent","confidence","reason","candidate_intents","operationalMemory"]}}$so$::jsonb
            ),
            '{OperationalMemory}',
            $om${"Schema":{"type":"object","additionalProperties":false,"properties":{"last_intent":{"type":"string","description":"Nome da última intent classificada pelo Router."},"last_reason":{"type":"string","description":"Razão curta (≤200 chars) da última classificação."},"clarification_depth":{"type":"integer","minimum":0,"description":"Turnos consecutivos com intent='needs_clarification'. Resetado a 0 quando o Router escolhe qualquer outra intent."},"last_candidate_intents":{"type":"array","description":"Nomes das candidate_intents do último turno needs_clarification. Vazio quando intent anterior != needs_clarification. Permite ao Router resolver clarification follow-up no turno seguinte.","items":{"type":"string"}}},"required":["last_intent","last_reason","clarification_depth","last_candidate_intents"]},"MaxBytes":2048}$om$::jsonb
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE ("Data"::jsonb->>'Type') = 'Router';

-- ─── (2) Backfill operational_memory: garante last_candidate_intents=[] ─────
-- Strict mode rejeita payload sem campo required. O middleware injeta o
-- payload no system prompt, então rows antigas precisam ganhar a chave nova
-- (default vazio) antes do código novo entrar no ar.
-- Coluna Payload é JSONB (ver schemas.sql linha 1668) — sem cast necessário.
UPDATE aihub.operational_memory
SET "Payload" = jsonb_set(
        "Payload",
        '{last_candidate_intents}',
        '[]'::jsonb,
        true
    ),
    "UpdatedAt" = NOW()
WHERE "Payload" IS NOT NULL
  AND ("Payload" -> 'last_candidate_intents') IS NULL;

COMMIT;
