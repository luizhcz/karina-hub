-- =============================================================================
-- 013_router_decision_telemetry_middleware.sql
--
-- Auto-injeta o middleware `RouterDecisionTelemetry` em TODOS os agentes
-- Router existentes. Em código novo o middleware é injetado por
-- AgentTemplateService.ApplyRouter no save; esta migration cobre o estado
-- atual do banco pra que Routers já persistidos ganhem telemetria sem
-- precisar re-salvar manualmente cada um.
--
-- Posição no array: PRIMEIRA entrada — pipeline wrap-from-inside-out faz com
-- que a primeira entrada do array vire o middleware mais interno, rodando
-- ANTES do StructuredOutputState (que emite STATE_DELTA via SSE) e do
-- Blocklist. Garantia: o STATE_DELTA recebe output já validado (rewrite de
-- needs_clarification inválido aplicado).
--
-- Idempotente: re-rodar não duplica entry (filtra existentes antes do
-- prepend). Pré-prod: pode reaplicar à vontade.
-- =============================================================================

BEGIN;

UPDATE aihub.agent_definitions
SET "Data" = (
        jsonb_set(
            "Data"::jsonb,
            '{Middlewares}',
            (
                -- 1. Prepend do entry canônico (Enabled=true, Settings={}).
                -- 2. Concat com middlewares existentes filtrando duplicatas
                --    pelo mesmo Type (case-insensitive via LOWER).
                jsonb_build_array(
                    jsonb_build_object(
                        'Type', 'RouterDecisionTelemetry',
                        'Enabled', true,
                        'Settings', '{}'::jsonb
                    )
                )
                || COALESCE(
                    (
                        SELECT jsonb_agg(m)
                        FROM jsonb_array_elements(("Data"::jsonb)->'Middlewares') m
                        WHERE LOWER(m->>'Type') <> 'routerdecisiontelemetry'
                    ),
                    '[]'::jsonb
                )
            )
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE ("Data"::jsonb->>'Type') = 'Router';

COMMIT;
