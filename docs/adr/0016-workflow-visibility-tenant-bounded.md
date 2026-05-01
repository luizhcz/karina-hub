# ADR 0016 — Workflow Visibility com Tenant Boundary

**Status:** Aceito
**Data:** 2026-05-01
**Contexto:** Fase 1 do épico "Workflows multi-projeto (federated agents)"

## Contexto

`WorkflowDefinition` já tinha o campo `Visibility ∈ {"project","global"}` no domínio
desde antes desta ADR, mas:

- A API REST não expunha o campo (`WorkflowResponse` omitia, `CreateWorkflowRequest` não aceitava).
- `PgWorkflowDefinitionRepository.ListVisibleAsync` usava `IgnoreQueryFilters()` + `r.Visibility=="global"` **sem boundary de tenant** — workflow global do tenant A era visível a todos os tenants.
- Não havia audit, telemetria, validação ou UI pra trocar visibility.
- `HasQueryFilter` no `AgentFwDbContext` só filtrava por `ProjectId`, ignorando workflows globais do tenant.

## Decisão

Workflows globais ficam **bounded por tenant**, com tooling completo (API, audit, métricas, UI).

### Modelo

- `AgentFwDbContext` injeta `ITenantContextAccessor` (mesmo pattern de `IProjectContextAccessor`).
- `WorkflowDefinitionRow` ganha coluna `TenantId VARCHAR(128) NOT NULL DEFAULT 'default'` (denormalizada de `projects.tenant_id`).
- `HasQueryFilter` passa a `(ProjectId == CurrentProjectId) OR (Visibility == "global" AND TenantId == CurrentTenantId)`.
- `IWorkflowDefinitionRepository.ListVisibleAsync(projectId, tenantId, ct)` aceita tenant explícito; query usa `IgnoreQueryFilters()` + filtro explícito (sem dependência do scope context).
- DDL idempotente em `db/schemas.sql`: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`, CHECK constraint via `NOT VALID + VALIDATE`, índice parcial `WHERE Visibility='global'`, backfill via JOIN com `aihub.projects`.

### API

- `PATCH /api/workflows/{id}/visibility` body `{visibility, reason?}` — endpoint dedicado.
- `WorkflowResponse` expõe `visibility`, `originProjectId`, `originTenantId`.
- `CreateWorkflowRequest` aceita `visibility?` (default "project").

### Owner gate

Somente requests no `ProjectId` dono podem alterar visibility. Caller de outro projeto → `403 Forbidden`.

### Audit

- Constante `AdminAuditActions.WorkflowVisibilityChanged = "workflow.visibility_changed"`.
- Payloads mínimos (`{visibility}` antes/depois) — não polui índices da `admin_audit_log`.
- `reason?` opcional vai no payloadAfter.

### Telemetry

- Counter OTel `workflows.visibility_changes_total{from, to, tenant}`.

### Cache

- Cache Redis `workflow-def:{id}` é **invalidado** (`RemoveAsync`) quando visibility muda no upsert. Outros projetos do tenant podem estar com cache stale; força re-fetch.
- Phase 1 cache continua por `id` apenas; Phase 2 estende a `agent-def:{tenantId}:{id}` (tenant-aware) por causa do agent sharing.

## Alternativas rejeitadas

1. **Visibility="public" cross-tenant** — quebra o boundary que define o produto multi-tenant. Rejeitado.
2. **Whitelist de projetos por workflow** (`AllowedProjectIds[]?`) — extrapola Phase 1; defer pra Phase 3 do épico.
3. **AdminGate pra alterar visibility** — operação é per-projeto; usuários comuns do owner devem poder ajustar. Decisão de produto: sem AdminGate.
4. **Cache pub/sub para invalidação cross-project** — overkill pra Phase 1; o `RemoveAsync` direto no Redis já é compartilhado entre instâncias.
5. **`IgnoreQueryFilters` com filtro só por Visibility (sem tenant)** — leak cross-tenant. Rejeitado.

## Consequências

- Trabalho gradual: Phase 1 entrega só workflow sharing; Phase 2 introduz agent sharing com fixes de credential/skill/MCP cross-project; Phase 3 introduz hardening (whitelist, pin, feature flags).
- DBs antigos precisam de backfill de `tenant_id`; `DatabaseBootstrapService` re-roda DDL idempotente no startup.
- Workflows JSON antigos sem `Visibility` deserializam pra `"project"` (default) — BC preservada.
- Cross-project cache é expurgado em mudança de visibility, mas reads idempotentes continuam respondendo do cache até a próxima mudança.

## Migration safety

- `ALTER TABLE ... ADD COLUMN ... DEFAULT 'default'` é metadata-only no PG 11+ (sem rewrite).
- CHECK constraint via `NOT VALID + VALIDATE` em transações separadas evita lock exclusivo > 100ms.
- Index parcial é criado com `IF NOT EXISTS`; rebuild não bloqueia escritas.

## Próximas fases (referência)

- **Fase 2** — Shared agents (`AgentDefinition.Visibility`, `LlmTokenUsageRow.OriginAgentProjectId`, fix de credential/skill/MCP no owner).
- **Fase 3** — Whitelist, pin de versão, feature flags `Sharing:*`, métricas/health/runbook.
