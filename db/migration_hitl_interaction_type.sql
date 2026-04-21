-- Migration: Adiciona InteractionType e Options à tabela human_interactions
-- P3-B: Separar tipos de interação (Approval, Input, Choice)
-- Idempotente: usa ADD COLUMN IF NOT EXISTS (PostgreSQL 9.6+)

SET search_path TO aihub;

ALTER TABLE human_interactions
    ADD COLUMN IF NOT EXISTS "InteractionType" VARCHAR(32) NOT NULL DEFAULT 'Approval';

ALTER TABLE human_interactions
    ADD COLUMN IF NOT EXISTS "Options" TEXT NULL;
