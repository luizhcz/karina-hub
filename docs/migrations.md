# Migrations — EfsAiHub

Lista cronológica das migrations SQL. Aplicar na ordem listada (todas
idempotentes com `IF NOT EXISTS` ou `ADD COLUMN IF NOT EXISTS`).

Pré-requisito: `db/schema.sql` já aplicado pelo menos uma vez.

---

## Aplicadas

| Data         | Arquivo                                              | Sumário |
|--------------|------------------------------------------------------|---------|
| 2025-xx-xx   | `migration_admin_audit_log.sql`                     | Tabela `admin_audit_log` com colunas TenantId/ProjectId/Actor/Action/Resource/Payload. |
| 2025-xx-xx   | `migration_audit_indexes.sql`                       | Índices compostos para queries de audit por tempo. |
| 2025-xx-xx   | `migration_composite_indexes.sql`                   | Índices adicionais de performance. |
| 2025-xx-xx   | `migration_document_intelligence.sql`               | Tabelas `document_extraction_*` + pricing. |
| 2025-xx-xx   | `migration_hitl_interaction_type.sql`               | Coluna `InteractionType` em HITL. |
| 2025-xx-xx   | `migration_hitl_resolved_by.sql`                    | Coluna `ResolvedBy` em HITL. |
| 2025-xx-xx   | `migration_mcp_servers.sql`                         | Tabela `mcp_servers`. |
| 2025-xx-xx   | `migration_rename_tools_pt_en.sql`                  | Rename de tools PT→EN. |
| 2025-xx-xx   | `migration_seed_agent_prompt_versions.sql`          | Seed inicial de versões de prompt. |
| 2026-04-23   | `migration_persona_scope_rename.sql`                | Rename de scope `global` → `global:cliente`/`global:admin`. One-shot. |
| 2026-04-23   | `migration_llm_token_usage_cached.sql`             | F1 — coluna `CachedTokens INT NOT NULL DEFAULT 0` em `llm_token_usage` pra capturar prompt caching do OpenAI. Ver [ADR 000](adr/000-opensdk-shape.md). |
| 2026-04-23   | `migration_composite_indexes.sql` (revisada)       | F3 — adicionado guard `DO $$ IF EXISTS columns THEN …` ao índice de `SequenceId` (coluna que nunca foi criada no schema corrente). Permite `apply.sh` rodar clean em ambientes novos. |
| 2026-04-23   | `migration_persona_template_length.sql`            | F3 — CHECK constraint `LENGTH("Template") <= 50000` em `persona_prompt_templates`. Protege contra admin salvando payload enorme via curl. |
| 2026-04-23   | `migration_project_id_columns.sql`                 | F4 — adiciona coluna `ProjectId VARCHAR(128) NULL` em `node_executions` e `llm_token_usage`; índices compostos `(ProjectId, ...)`. Também limpa coluna `TenantId` dessas tabelas + `conversations` (pivot abortado, ver [ADR 003](adr/003-project-as-tenancy-boundary.md)). |
| **2026-04-23** | **`migration_persona_template_versions.sql`**     | **F5 — cria tabela `persona_prompt_template_versions` (append-only) + coluna `ActiveVersionId UUID NULL` em `persona_prompt_templates` + backfill de uma version inicial pra cada template existente.** |
| 2026-04-23 | `migration_project_id_backfill.sql`                | F5.5 — backfill de rows legadas pré-F4 com `ProjectId IS NULL` (13640 `node_executions` + 8190 `llm_token_usage`) pra `'default'`. Idempotente. Precede remoção do `\|\| ProjectId == null` no `HasQueryFilter` (backlog TENANCY-STRICT-FILTER). |
| **2026-04-23** | **`migration_persona_experiments.sql`**           | **F6 — cria tabela `persona_prompt_experiments` com UNIQUE parcial `(ProjectId, Scope) WHERE EndedAt IS NULL` garantindo 1 experiment ativo por scope. Ver [ADR 005](adr/005-persona-ab-testing.md).** |
| **2026-04-23** | **`migration_llm_token_usage_experiment.sql`**    | **F6 — adiciona `ExperimentId INT NULL` + `ExperimentVariant CHAR(1) NULL CHECK IN ('A','B')` em `llm_token_usage` + índice parcial em `ExperimentId IS NOT NULL` pra aggregate por variant. Habilita dashboard de resultados.** |
| **(pendente)** | **`migration_persona_templates_drop_updatedby.sql`** | **F9 — DROP da coluna `UpdatedBy` de `persona_prompt_templates`. NÃO aplicar na mesma release que remove o UpdatedBy do código; só depois de ≥ 1 deploy cycle validando app ignorando a coluna. Ver [ADR 008](adr/008-persona-updatedby-deprecation.md).** |

---

## Ordem de aplicação (novo ambiente)

Use o wrapper `db/apply.sh` (introduzido na F3). Ele ordena schema →
migrations → seeds alfabeticamente e para no primeiro erro. Ver
[docs/deploy.md](deploy.md) para detalhes.

---

## Template de entrada nova

```
| YYYY-MM-DD | `migration_<assunto>.sql` | Descrição curta. Link pra ADR/issue se relevante. |
```
