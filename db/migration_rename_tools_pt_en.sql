-- Migration: renomeia tools/executors PT → EN em agent_definitions, agent_prompt_versions
-- e workflow_definitions. Idempotente via REPLACE — rodar várias vezes é seguro.
-- Cobre nomes em:
--   agent_definitions.Data::text (Tools[].Name + Instructions + Metadata)
--   agent_prompt_versions.Content (texto do prompt que cita a tool por nome)
--   agent_versions.Snapshot::text (snapshot imutável — opcional, reescreve só para
--     evitar warning de fingerprint mismatch; comportamento funcional não muda)
--   workflow_definitions.Data::text (Executors[].FunctionName)
--
--   psql -f db/migration_rename_tools_pt_en.sql
--
-- Sem alias BC: ambientes em dev. Em prod, considerar feature flag primeiro.

BEGIN;

-- Helper: replace sequencial dos 8 pares PT→EN num texto/jsonb.
-- Aplicado via UPDATE direto com CHAIN de REPLACE (SQL padrão).

UPDATE aihub.agent_definitions
SET "Data" = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    "Data",
    'buscar_ativo', 'search_asset'),
    'ObterPosicaoCliente', 'get_asset_position'),
    'ConsultarCarteira', 'get_portfolio'),
    'ExecutarResgate', 'redeem_asset'),
    'ExecutarAplicacao', 'invest_asset'),
    'CalcularIrResgate', 'calculate_asset_redemption_tax'),
    'atendimento_pre_processor', 'service_pre_processor'),
    'atendimento_post_processor', 'service_post_processor')
WHERE "Data" ~ '(buscar_ativo|ObterPosicaoCliente|ConsultarCarteira|ExecutarResgate|ExecutarAplicacao|CalcularIrResgate|atendimento_(pre|post)_processor)';

UPDATE aihub.agent_prompt_versions
SET "Content" = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    "Content",
    'buscar_ativo', 'search_asset'),
    'ObterPosicaoCliente', 'get_asset_position'),
    'ConsultarCarteira', 'get_portfolio'),
    'ExecutarResgate', 'redeem_asset'),
    'ExecutarAplicacao', 'invest_asset'),
    'CalcularIrResgate', 'calculate_asset_redemption_tax'),
    'atendimento_pre_processor', 'service_pre_processor'),
    'atendimento_post_processor', 'service_post_processor')
WHERE "Content" ~ '(buscar_ativo|ObterPosicaoCliente|ConsultarCarteira|ExecutarResgate|ExecutarAplicacao|CalcularIrResgate|atendimento_(pre|post)_processor)';

UPDATE aihub.agent_versions
SET "Snapshot" = REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
    "Snapshot"::text,
    'buscar_ativo', 'search_asset'),
    'ObterPosicaoCliente', 'get_asset_position'),
    'ConsultarCarteira', 'get_portfolio'),
    'ExecutarResgate', 'redeem_asset'),
    'ExecutarAplicacao', 'invest_asset'),
    'CalcularIrResgate', 'calculate_asset_redemption_tax'),
    'atendimento_pre_processor', 'service_pre_processor'),
    'atendimento_post_processor', 'service_post_processor')::jsonb
WHERE "Snapshot"::text ~ '(buscar_ativo|ObterPosicaoCliente|ConsultarCarteira|ExecutarResgate|ExecutarAplicacao|CalcularIrResgate|atendimento_(pre|post)_processor)';

UPDATE aihub.workflow_definitions
SET "Data" = REPLACE(REPLACE(
    "Data",
    'atendimento_pre_processor', 'service_pre_processor'),
    'atendimento_post_processor', 'service_post_processor')
WHERE "Data" ~ 'atendimento_(pre|post)_processor';

COMMIT;

-- Sanity: conferir que zerou
-- SELECT count(*) FROM aihub.agent_definitions WHERE "Data" ~ '(buscar_ativo|ObterPosicaoCliente|ConsultarCarteira|ExecutarResgate|ExecutarAplicacao|CalcularIrResgate|atendimento_(pre|post)_processor)';
