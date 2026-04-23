-- Migration: Indexes compostos para queries de analytics e SSE replay
-- Criados com CONCURRENTLY para não bloquear leitura/escrita durante criação.
--
-- Guards: cada index checa se a coluna alvo existe antes de tentar criar — a
-- migração legacy referenciava `SequenceId` em `workflow_event_audit` que não
-- chegou a ser criada no schema corrente (a tabela usa `Id` bigint como
-- sequence natural). O guard evita quebrar ambientes novos via apply.sh
-- enquanto preserva o índice em ambientes que porventura já tenham `SequenceId`.

-- Analytics: tokens por agente no tempo (GROUP BY AgentId, ModelId WHERE CreatedAt > X)
CREATE INDEX CONCURRENTLY IF NOT EXISTS
  idx_llm_token_usage_agent_model_created
  ON aihub.llm_token_usage ("AgentId", "ModelId", "CreatedAt" DESC);

-- SSE replay: eventos por execução ordenados por sequência.
-- Só cria o índice se `SequenceId` ainda existir (ambiente legacy). Em ambiente
-- corrente, o índice `IX_workflow_event_audit_ExecutionId_Id` (schema.sql) já
-- cobre o mesmo acesso usando `Id` como sequence.
DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema = 'aihub'
          AND table_name = 'workflow_event_audit'
          AND column_name = 'SequenceId'
    ) THEN
        EXECUTE 'CREATE INDEX CONCURRENTLY IF NOT EXISTS
                 idx_workflow_event_audit_exec_seq
                 ON aihub.workflow_event_audit ("ExecutionId", "SequenceId" DESC)';
    END IF;
END
$$;
