-- Migration: cria aihub.mcp_servers — registry centralizado de servidores MCP
-- (Model Context Protocol). Agents passam a referenciar MCPs pelo Id deste registro
-- em vez de duplicar inline serverLabel/serverUrl/allowedTools/headers.
-- Idempotente: CREATE TABLE IF NOT EXISTS + CREATE INDEX IF NOT EXISTS.
--   psql -f db/migration_mcp_servers.sql

CREATE TABLE IF NOT EXISTS aihub.mcp_servers (
    "Id"         VARCHAR(128) NOT NULL,
    "Name"       VARCHAR(256) NOT NULL,
    "Data"       JSONB        NOT NULL,              -- McpServer serializado (ServerLabel, ServerUrl, AllowedTools, Headers, RequireApproval, Description)
    "ProjectId"  VARCHAR(128) NOT NULL DEFAULT 'default',
    "CreatedAt"  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt"  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    CONSTRAINT "PK_mcp_servers" PRIMARY KEY ("Id")
);

-- Índice composto cobre listagem "MCPs do meu projeto ordenados por nome" (tela /mcp-servers).
CREATE INDEX IF NOT EXISTS "IX_mcp_servers_ProjectId_Name"
    ON aihub.mcp_servers ("ProjectId", "Name");
