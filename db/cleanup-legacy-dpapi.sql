-- ─────────────────────────────────────────────────────────────────────────────
-- Cleanup: remove campos legacy DPAPI do llm_config JSONB
-- ─────────────────────────────────────────────────────────────────────────────
-- Pré-condição: PR 5 do épico AWS Secrets Manager mergeado em prod e métrica
-- secrets.legacy_dpapi_resolutions_total em zero por ≥7 dias (telemetria
-- observada manualmente). Após PR 5, decifragem DPAPI foi removida do
-- PgProjectRepository — projetos que ainda tinham apiKeyCipher passam a
-- retornar ApiKey = null (e a UI força recadastro com referência AWS).
--
-- Este script é idempotente — pode ser rodado múltiplas vezes sem efeito
-- colateral. Operação por projeto:
--   - Remove `apiKeyCipher` e `keyVersion` de cada credencial em llm_config.
--   - Mantém `endpoint` e `secretRef` intactos.
--
-- Como rodar:
--   docker compose exec postgres psql -U efs_ai_hub -d efs_ai_hub -f /tmp/cleanup-legacy-dpapi.sql
--
-- ATENÇÃO: aplicar APENAS após validar que todos os projetos foram recadastrados
-- via UI com referências AWS Secrets Manager.

BEGIN;

-- 1. Conta projetos afetados (visibilidade pré-cleanup).
SELECT
    id,
    name,
    jsonb_object_keys(llm_config->'credentials') AS provider,
    llm_config->'credentials'->jsonb_object_keys(llm_config->'credentials')->>'apiKeyCipher' IS NOT NULL AS has_legacy_cipher,
    llm_config->'credentials'->jsonb_object_keys(llm_config->'credentials')->>'secretRef' IS NOT NULL AS has_aws_ref
FROM aihub.projects
WHERE llm_config IS NOT NULL
  AND jsonb_typeof(llm_config->'credentials') = 'object';

-- 2. Limpa campos legacy de cada credential, preservando o resto.
UPDATE aihub.projects p
SET llm_config = jsonb_set(
        llm_config,
        '{credentials}',
        (
            SELECT jsonb_object_agg(
                provider_key,
                provider_data - 'apiKeyCipher' - 'keyVersion'
            )
            FROM jsonb_each(llm_config->'credentials') AS e(provider_key, provider_data)
        )
    )
WHERE jsonb_typeof(llm_config->'credentials') = 'object'
  AND EXISTS (
      SELECT 1
      FROM jsonb_each(llm_config->'credentials') AS e(_, provider_data)
      WHERE provider_data ? 'apiKeyCipher' OR provider_data ? 'keyVersion'
  );

-- 3. Confirma resultado (esperado: 0 projetos com apiKeyCipher remanescente).
SELECT count(*) AS projects_with_legacy_remaining
FROM aihub.projects p,
     jsonb_each(p.llm_config->'credentials') AS e(_, provider_data)
WHERE provider_data ? 'apiKeyCipher' OR provider_data ? 'keyVersion';

COMMIT;
