-- Migration: Indexes compostos para queries de analytics e SSE replay
-- Criados com CONCURRENTLY para não bloquear leitura/escrita durante criação.

-- Analytics: tokens por agente no tempo (GROUP BY AgentId, ModelId WHERE CreatedAt > X)
CREATE INDEX CONCURRENTLY IF NOT EXISTS
  idx_llm_token_usage_agent_model_created
  ON aihub.llm_token_usage ("AgentId", "ModelId", "CreatedAt" DESC);

-- SSE replay: eventos por execução ordenados por sequência
CREATE INDEX CONCURRENTLY IF NOT EXISTS
  idx_workflow_event_audit_exec_seq
  ON aihub.workflow_event_audit ("ExecutionId", "SequenceId" DESC);
