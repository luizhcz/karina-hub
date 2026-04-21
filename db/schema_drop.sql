-- =============================================================================
-- EfsAiHub — Drop DDL (rollback completo)
-- PostgreSQL 16+ · Schema: aihub
--
-- ATENÇÃO: Este script remove TODAS as tabelas e o schema aihub com todos os dados.
-- Execute apenas em ambientes de desenvolvimento ou com backup confirmado.
--
-- Uso:
--   psql -U <usuario> -d <banco> -f schema_drop.sql
-- =============================================================================

DROP TABLE IF EXISTS aihub.tool_invocations;
DROP TABLE IF EXISTS aihub.llm_token_usage;
DROP TABLE IF EXISTS aihub.workflow_event_audit;
DROP TABLE IF EXISTS aihub.workflow_checkpoints;
DROP TABLE IF EXISTS aihub.node_executions;
DROP TABLE IF EXISTS aihub.workflow_executions;
DROP TABLE IF EXISTS aihub.human_interactions;
DROP TABLE IF EXISTS aihub.background_response_jobs;
DROP TABLE IF EXISTS aihub.agent_sessions;
DROP TABLE IF EXISTS aihub.chat_messages;
DROP TABLE IF EXISTS aihub.conversations;
DROP TABLE IF EXISTS aihub.skill_versions;
DROP TABLE IF EXISTS aihub.skills;
DROP TABLE IF EXISTS aihub.agent_prompt_versions;
DROP TABLE IF EXISTS aihub.workflow_versions;
DROP TABLE IF EXISTS aihub.workflow_definitions;
DROP TABLE IF EXISTS aihub.agent_versions;
DROP TABLE IF EXISTS aihub.agent_definitions;
DROP TABLE IF EXISTS aihub.model_pricing;
DROP TABLE IF EXISTS aihub.ativos;
DROP TABLE IF EXISTS aihub.projects;

DROP SCHEMA IF EXISTS aihub;
