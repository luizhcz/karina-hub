-- Migration: Audit indexes for hot-path queries
-- Executar via psql (CONCURRENTLY requer execução fora de transaction)
-- psql -f db/migration_audit_indexes.sql

-- Covering index: execuções recentes de um projeto (evita sort extra)
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_workflow_executions_project_status_started
  ON aihub.workflow_executions ("ProjectId", "Status", "StartedAt" DESC);

-- Conversations: listagem admin por projeto + recência
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_conversations_project_last_message
  ON aihub.conversations ("ProjectId", "LastMessageAt" DESC);

-- Conversations: histórico por usuário
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_conversations_user_created
  ON aihub.conversations ("UserId", "CreatedAt" DESC);

-- HITL: pending approvals por execução (hot path)
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_human_interactions_execution_status
  ON aihub.human_interactions ("ExecutionId", "Status");

-- Chat messages: join em ExecutionFullDetail
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_chat_messages_execution
  ON aihub.chat_messages ("ExecutionId");

-- EF Core query filters por ProjectId
CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_agent_definitions_project
  ON aihub.agent_definitions ("ProjectId");

CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_workflow_definitions_project
  ON aihub.workflow_definitions ("ProjectId");
