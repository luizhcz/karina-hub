-- =============================================================================
-- 019_webhook_deliveries.sql
--
-- Tabela de tracking de entregas de webhook do pool standalone. Um job
-- terminal (Completed/Failed) com CallbackTarget != null gera uma row aqui,
-- consumida pelo WebhookCallbackDeliveryService.
--
-- Sem retry no design atual: uma única tentativa POST. Status terminal já no
-- primeiro response — Delivered (2xx) ou Failed (qualquer outra coisa). Sem
-- coluna NextAttemptAt nem job de retomada — se o webhook falhar, cliente
-- precisa pollar /responses/{jobId} pra reconciliar.
--
-- HMAC secret armazenado em texto claro — operador com SELECT na tabela vê o
-- segredo. Tech debt: encriptar via Data Protection antes de prod externa.
--
-- Idempotente: CREATE IF NOT EXISTS.
-- =============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS aihub.webhook_deliveries (
    "DeliveryId"       VARCHAR(64)  NOT NULL,
    "JobId"            VARCHAR(64)  NOT NULL,                   -- FK lógica pra background_response_jobs
    "Url"              TEXT         NOT NULL,
    "HmacSecret"       TEXT         NULL,                       -- texto claro; tech debt: encriptar
    "Headers"          JSONB        NULL,                       -- headers extras opcionais
    "Status"           VARCHAR(32)  NOT NULL DEFAULT 'Pending', -- Pending | Delivered | Failed
    "LastResponseCode" INTEGER      NULL,
    "LastError"        TEXT         NULL,
    "DeliveredAt"      TIMESTAMPTZ  NULL,
    "ProjectId"        VARCHAR(128) NOT NULL DEFAULT 'default', -- denormalizado do job pra autorização
    "TenantId"         VARCHAR(128) NOT NULL DEFAULT 'default',
    "CreatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"        TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_webhook_deliveries" PRIMARY KEY ("DeliveryId")
);

-- Hot path do worker: pegar pendentes em ordem de criação via FOR UPDATE SKIP LOCKED.
CREATE INDEX IF NOT EXISTS "IX_webhook_deliveries_Status_CreatedAt"
    ON aihub.webhook_deliveries ("Status", "CreatedAt")
    WHERE "Status" = 'Pending';

-- Lookup por JobId pro endpoint GET /responses/{jobId}/deliveries.
CREATE INDEX IF NOT EXISTS "IX_webhook_deliveries_JobId"
    ON aihub.webhook_deliveries ("JobId");

COMMIT;
