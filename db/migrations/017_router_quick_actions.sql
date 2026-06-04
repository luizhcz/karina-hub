-- =============================================================================
-- 017_router_quick_actions.sql
--
-- Atalhos determinísticos por Router pra bypass do classificador LLM. Quando
-- o `Pattern` (normalizado) bate na mensagem do usuário, o Router retorna a
-- `Intent` diretamente — zero chamada LLM, zero llm_token_usage row.
--
-- Sintaxe do Pattern (validada na API):
--   - Texto lowercase normalizado (trim + collapsed whitespace).
--   - Sufixo " *" opcional no fim significa "1+ token livre".
--   - Sem regex aberto — sintaxe controlada, sem risco de ReDoS.
--
-- Idempotente: CREATE IF NOT EXISTS em tudo.
-- =============================================================================

BEGIN;

CREATE TABLE IF NOT EXISTS aihub.router_quick_actions (
    "Id"           VARCHAR(64)  NOT NULL,
    "RouterId"     VARCHAR(256) NOT NULL,                          -- FK lógica pra agent_definitions.Id
    "Pattern"      VARCHAR(512) NOT NULL,                          -- normalizado (lowercase), com '*' opcional no fim
    "DisplayText"  VARCHAR(512) NOT NULL,                          -- texto exibido na UI (preserva casing)
    "Intent"       VARCHAR(128) NOT NULL,                          -- intent retornada (validada contra agent_router_intents)
    "Description"  VARCHAR(1024) NULL,                             -- tooltip opcional
    "ProjectId"    VARCHAR(128) NOT NULL,
    "TenantId"     VARCHAR(128) NOT NULL DEFAULT 'default',
    "CreatedAt"    TIMESTAMPTZ  NOT NULL,
    "UpdatedAt"    TIMESTAMPTZ  NOT NULL,
    CONSTRAINT "PK_router_quick_actions" PRIMARY KEY ("Id"),
    -- Unique por (Tenant, Project, Router, Pattern) — Router Visibility=global
    -- pode ter atalhos diferentes em projetos diferentes do mesmo tenant.
    CONSTRAINT "UX_router_quick_actions_Scope_Router_Pattern"
        UNIQUE ("TenantId", "ProjectId", "RouterId", "Pattern")
);

CREATE INDEX IF NOT EXISTS "IX_router_quick_actions_RouterId_TenantId"
    ON aihub.router_quick_actions ("RouterId", "TenantId");

CREATE INDEX IF NOT EXISTS "IX_router_quick_actions_TenantId_ProjectId"
    ON aihub.router_quick_actions ("TenantId", "ProjectId");

COMMIT;
