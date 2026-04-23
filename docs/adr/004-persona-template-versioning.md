# ADR 004 — Persona template versioning: append-only em vez de flag `IsActive`

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 5

## Context

A feature de versionamento de templates de persona (F5) precisava decidir
entre duas estratégias já presentes no repo:

1. **`AgentVersion` / `SkillVersion`** — append-only + FK
   `Definition.CurrentVersionId` apontando pra version ativa; rollback
   **repointa** o ponteiro pra uma version antiga (sem criar linha nova).

2. **`AgentPromptVersion`** — flat com `IsActive` boolean; cada versão
   cadastrada é uma row; `SetMasterAsync(agentId, versionId)` move o
   `IsActive` de uma pra outra.

Ambas são pattern válidos no repo. A escolha afeta auditoria e
interpretação do histórico.

## Decision

**Append-only com `ActiveVersionId` apontando pra version criada a cada
mudança.** Rollback **cria nova version** com conteúdo da alvo (não
repointa pra antiga).

### Por quê

- **Audit trail linear e imutável**: cada linha no histórico reflete
  uma ação discreta do admin (edit ou rollback), com timestamp e
  `ChangeReason`. Reverter "v2" em 2025 e depois reverter "v3" em 2026
  gera 5 linhas em ordem temporal — fácil de explicar em auditoria.

- **Rollback é edit**: conceitualmente, "voltar a versão de 3 dias
  atrás" é equivalente a "editar o template com o conteúdo de 3 dias
  atrás". Os dois casos geram nova row. Não há distinção especial entre
  "edit manual" e "rollback" além de `ChangeReason` opcional.

- **`ActiveVersionId` nunca aponta pro passado**: garantia de que
  qualquer query `SELECT v.Template FROM versions WHERE VersionId =
  template.ActiveVersionId` sempre retorna o estado atual, sem pular
  pra linha com timestamp antigo (que confundiria dashboards).

- **Compat com pattern `AgentPromptVersion`**: usamos estrutura flat
  semelhante; a única diferença é que em vez de `IsActive bool` no
  version row, temos `ActiveVersionId UUID` no template. Menos escritas
  concorrentes (1 row no update, em vez de 2: desativar antiga + ativar
  nova).

### Alternativas rejeitadas

- **Repointar `ActiveVersionId` pra version antiga em rollback**:
  mais barato (0 bytes novos), mas histórico vira não-linear — a "ação
  de rollback" não aparece como linha própria; só vendo `Timestamp` em
  `admin_audit_log` pra inferir que houve rollback. Rejeitado.

- **`IsActive bool` no version row**: obriga 2 writes em transação
  (`UPDATE ... SET IsActive = false WHERE IsActive = true; INSERT ...
  IsActive = true`). O risco de race com concurrent updates (2 admins
  editando ao mesmo tempo) é maior. `ActiveVersionId` no template é
  escrita única.

## Consequences

**Positivo:**
- Histórico auditável sem lookup cruzado em `admin_audit_log`.
- Cada linha de `persona_prompt_template_versions` conta uma ação
  discreta — fácil de paginar e renderizar em UI.
- Rollback é implementável em uma transação curta (append version +
  update `ActiveVersionId` do template).

**Negativo:**
- Tabela cresce com cada edit + rollback. Em tenants com churn alto de
  templates, pode ser preciso retention (ex: manter últimas 100 versions
  + compactar rest). Fica como follow-up `PERSONA-VER-RETENTION` quando
  algum tenant reclamar.
- Rollback gera linha "duplicada" em termos de conteúdo (template
  idêntico a uma row anterior). Aceitável — é raro e o ganho em
  simplicidade de modelo compensa.

## References

- `db/migration_persona_template_versions.sql`
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/PersonaPromptTemplateVersion.cs`
- `src/EfsAiHub.Infra.Persistence/Postgres/PgPersonaPromptTemplateRepository.cs` (`UpsertAsync` + `RollbackAsync`)
- `src/EfsAiHub.Host.Api/Controllers/PersonaPromptTemplatesAdminController.cs` (`GetVersions` + `Rollback`)
- `frontend/src/features/admin/PersonaTemplateVersionsPage.tsx`
