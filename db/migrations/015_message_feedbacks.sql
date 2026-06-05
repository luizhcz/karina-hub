-- =============================================================================
-- 015_message_feedbacks.sql
--
-- Adiciona a tabela `aihub.message_feedbacks` para armazenar feedback (like/
-- dislike) do usuário sobre mensagens de assistente.
--
-- Modelo:
--  - Sentiment em int: +1 (like) | -1 (dislike). Outros valores são bloqueados
--    pelo CHECK constraint; a facade valida antes do save.
--  - Upsert lógico por (UserId, MessageId) — uma opinião por usuário por
--    mensagem. Mudar o sentiment ou o comment atualiza o registro existente.
--  - Sem denormalizar agente: ConversationId + MessageId já permitem
--    reconstruir o histórico completo (via /messages e /full). Quem precisa
--    do agente que respondeu navega ChatMessage.ExecutionId → execution.
--  - ProjectId acompanha o padrão multi-tenancy do schema (HasQueryFilter).
--
-- Idempotente: CREATE IF NOT EXISTS em tudo.
-- =============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS aihub.message_feedbacks (
    "FeedbackId"     VARCHAR(64)  NOT NULL,
    "MessageId"      VARCHAR(64)  NOT NULL,
    "ConversationId" VARCHAR(64)  NOT NULL,
    "UserId"         VARCHAR(256) NOT NULL,
    "Sentiment"      INTEGER      NOT NULL,
    "Comment"        VARCHAR(2000) NULL,
    "ProjectId"      VARCHAR(128) NOT NULL DEFAULT 'default',
    "CreatedAt"      TIMESTAMPTZ  NOT NULL,
    "UpdatedAt"      TIMESTAMPTZ  NULL,
    CONSTRAINT "PK_message_feedbacks" PRIMARY KEY ("FeedbackId"),
    CONSTRAINT "CK_message_feedbacks_Sentiment" CHECK ("Sentiment" IN (-1, 1))
);

CREATE UNIQUE INDEX IF NOT EXISTS "UX_message_feedbacks_UserId_MessageId"
    ON aihub.message_feedbacks ("UserId", "MessageId");

CREATE INDEX IF NOT EXISTS "IX_message_feedbacks_MessageId"
    ON aihub.message_feedbacks ("MessageId");

CREATE INDEX IF NOT EXISTS "IX_message_feedbacks_ConversationId"
    ON aihub.message_feedbacks ("ConversationId");

CREATE INDEX IF NOT EXISTS "IX_message_feedbacks_CreatedAt"
    ON aihub.message_feedbacks ("CreatedAt");

COMMIT;
