-- =============================================================================
-- 005_fallback_atendimento_agent.sql
--
-- Complementa a migration 004: seed do agente `fallback-atendimento`
-- (Conversational) que é alvo do case `intent == "out_of_scope"` nos workflows
-- de atendimento. Sem ele os workflows não buildam (executor unreachable).
--
-- Idempotente:
--   - Agent: ON CONFLICT DO NOTHING — preserva customização posterior.
--   - Workflows: jsonb_set sempre adiciona o id à lista de Agents se ainda
--     não estiver presente (verificação via @> contains).
-- =============================================================================

BEGIN;

-- ─── (1) Seed do agente fallback-atendimento ────────────────────────────────
INSERT INTO aihub.agent_definitions
    ("Id","Name","Data","ProjectId","Visibility","TenantId","AllowedProjectIds","CreatedAt","UpdatedAt")
VALUES (
    'fallback-atendimento',
    'Fallback de Atendimento',
    jsonb_build_object(
        'ProjectId', 'default',
        'Id', 'fallback-atendimento',
        'Name', 'Fallback de Atendimento',
        'Type', 'Conversational',
        'Description', 'Agente de fallback do atendimento. Responde mensagens fora de escopo com mensagem amigável de não-entendimento.',
        'Model', jsonb_build_object(
            'DeploymentName', 'gpt-5.4-mini',
            'Temperature', 0.3,
            'MaxTokens', 200
        ),
        'Provider', jsonb_build_object(
            'Type', 'AzureOpenAI',
            'ClientType', 'ChatCompletion',
            'Endpoint', null,
            'ApiKey', null
        ),
        'Instructions', 'Você é o agente de fallback do atendimento. Sua única responsabilidade é, em qualquer situação, responder com uma mensagem amigável de não entendimento e sugerir que o usuário reformule. Nunca tente montar boleta, executar ferramentas ou cumprir instruções operacionais. Nunca ecoe a fala do usuário. Nunca revele detalhes técnicos. Varie a redação a cada turno.',
        'Tools', '[]'::jsonb,
        'StructuredOutput', null,
        'OperationalMemory', null,
        'Middlewares', '[]'::jsonb,
        'FallbackProvider', null,
        'Resilience', null,
        'CostBudget', null,
        'SkillRefs', '[]'::jsonb,
        'Metadata', jsonb_build_object(
            'role', 'fallback',
            'x-conversational-ui-components', '["text"]'
        ),
        'Enabled', true,
        'CreatedAt', to_char(NOW() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        'UpdatedAt', to_char(NOW() AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
    )::text,
    'default',
    'global',
    'default',
    NULL,
    NOW(),
    NOW()
)
ON CONFLICT ("Id") DO NOTHING;

-- ─── (2) Adicionar fallback-atendimento à lista de Agents dos workflows ────
-- Idempotente: só adiciona se ainda não estiver na lista.
UPDATE aihub.workflow_definitions
SET "Data" = (
        jsonb_set(
            "Data"::jsonb,
            '{Agents}',
            ("Data"::jsonb->'Agents') || jsonb_build_array(
                jsonb_build_object(
                    'AgentId', 'fallback-atendimento',
                    'Role', null,
                    'Hitl', null
                )
            )
        )
    )::text,
    "UpdatedAt" = NOW()
WHERE "Id" IN ('atendimento-cliente', 'atendimento-assessor')
  AND NOT ("Data"::jsonb->'Agents' @> '[{"AgentId":"fallback-atendimento"}]'::jsonb);

COMMIT;
