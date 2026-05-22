-- =============================================================================
-- 011_conversational_output_type_status.sql
--
-- Migra agentes Conversational existentes do shape antigo
-- { ui_component, message, output } pro novo { output_type, output_status,
-- message, output }. Operação 2-em-1 no jsonb do Data:
--   1. Renomeia a metadata key 'x-conversational-ui-components' →
--      'x-conversational-output-statuses' preservando a lista atual.
--   2. Injeta 'x-conversational-output-type' = "text" quando ausente.
--
-- Pós-migration, o startup do backend re-recompõe todo agente via
-- AgentVersionBackfillService — o snapshot canônico em agent_versions é
-- regravado com o schema novo.
--
-- Aplicar manualmente em homolog/prod ANTES do deploy do código novo —
-- caso contrário o boot falha porque AgentTemplateService.ReadOutputStatuses
-- já espera a nova chave.
-- Idempotente: re-rodar não muda nada (chaves novas já existentes preservam).
-- =============================================================================

UPDATE aihub.agent_definitions
SET "Data" = (
    -- Etapa 1: injeta x-conversational-output-statuses copiando da chave
    -- legada (quando existe); jsonb_set create_if_missing=true cria se ausente.
    (
        CASE
            WHEN ("Data"::jsonb #> '{Metadata,x-conversational-output-statuses}') IS NOT NULL THEN
                "Data"::jsonb
            WHEN ("Data"::jsonb #> '{Metadata,x-conversational-ui-components}') IS NOT NULL THEN
                jsonb_set(
                    "Data"::jsonb,
                    '{Metadata,x-conversational-output-statuses}',
                    "Data"::jsonb #> '{Metadata,x-conversational-ui-components}',
                    true
                )
            ELSE
                jsonb_set(
                    "Data"::jsonb,
                    '{Metadata,x-conversational-output-statuses}',
                    '"[\"default\"]"'::jsonb,
                    true
                )
        END
    )
    -- Etapa 2: remove a chave legada (no-op se já não existir).
    #- '{Metadata,x-conversational-ui-components}'
)::text
WHERE "Data"::jsonb #>> '{Type}' = 'Conversational'
  AND (
    ("Data"::jsonb #> '{Metadata,x-conversational-output-statuses}') IS NULL
    OR ("Data"::jsonb #> '{Metadata,x-conversational-ui-components}') IS NOT NULL
  );

UPDATE aihub.agent_definitions
SET "Data" = jsonb_set(
    "Data"::jsonb,
    '{Metadata,x-conversational-output-type}',
    '"text"'::jsonb,
    true
)::text
WHERE "Data"::jsonb #>> '{Type}' = 'Conversational'
  AND ("Data"::jsonb #> '{Metadata,x-conversational-output-type}') IS NULL;
