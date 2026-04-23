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
| **2026-04-23** | **`migration_llm_token_usage_cached.sql`**         | **F1 — coluna `CachedTokens INT NOT NULL DEFAULT 0` em `llm_token_usage` pra capturar prompt caching do OpenAI. Ver [ADR 000](adr/000-opensdk-shape.md).** |

---

## Ordem de aplicação (novo ambiente)

1. `db/schema.sql`
2. Todas as `db/migration_*.sql` em ordem alfabética (ou cronológica — as datas nos nomes são ordenação robusta)
3. Todas as `db/seed_*.sql` em ordem alfabética

Quando `db/apply.sh` for introduzido (Fase 3 do roadmap persona), usar
esse script ao invés de `psql` manual.

---

## Template de entrada nova

```
| YYYY-MM-DD | `migration_<assunto>.sql` | Descrição curta. Link pra ADR/issue se relevante. |
```
