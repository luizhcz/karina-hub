# Multi-Tenant — Inventário de Repos e Isolamento

Decisão: **ProjectId como boundary de isolamento** (ver
[ADR 003](adr/003-project-as-tenancy-boundary.md)).

Este documento rastreia qual repositório tem filtro por `ProjectId` e
qual fica intencionalmente sem (queries admin-only autorizadas por
RBAC).

---

## Entidades filtradas via `HasQueryFilter` no DbContext

Filter: `e.ProjectId == CurrentProjectId` (ou tolerante ao null em rows
pré-F4).

| Entidade                | Source                                                     | F4? |
|-------------------------|------------------------------------------------------------|-----|
| `ConversationSession`   | `AgentFwDbContext.OnModelCreating`                         | —   |
| `WorkflowDefinitionRow` | idem                                                       | —   |
| `AgentDefinitionRow`    | idem                                                       | —   |
| `SkillRow`              | idem                                                       | —   |
| `WorkflowExecutionRow`  | idem                                                       | —   |
| `McpServerRow`          | idem                                                       | —   |
| `NodeExecutionRow`      | `AgentFwDbContext.OnModelCreating`                         | **✓** |
| `LlmTokenUsageRow`      | `AgentFwDbContext.OnModelCreating`                         | **✓** |

---

## Repos raw-SQL (Npgsql direto)

Lista completa dos `Pg*Repository.cs` e estado de filtragem por project.

| Repositório                              | SELECTs | Filtra ProjectId? | Observação |
|------------------------------------------|---------|-------------------|------------|
| `PgProjectRepository`                    | 3       | ✓ (por tenant)    | `GetByTenantAsync` filtra por `tenant_id`. |
| `PgAdminAuditLogRepository`              | 2       | ✓ opcional        | `QueryAsync` aceita `ProjectId`/`TenantId` no filtro. |
| `PgBackgroundResponseRepository`         | ?       | ⚠️ não auditado   | Follow-up. |
| `PgLlmTokenUsageRepository`              | 4       | ✓ via EF          | Operações EF herdam `HasQueryFilter`. Raw SQL em `GetAllAgentsSummaryAsync`, `GetThroughputAsync` e similares são **admin-only** (bypass intencional). |
| `PgPersonaPromptTemplateRepository`      | 4 + versões | F5.5 via controller | Templates são global/agent/project-scoped **via Scope string**, não coluna. `PersonaPromptTemplateVersionRow` (F5) também não tem `HasQueryFilter` próprio (FK CASCADE herda isolamento do pai). **Enforcement real**: `PersonaPromptTemplatesAdminController.IsScopeAccessibleByCurrentProject` valida scope contra project corrente em todos os endpoints (`GetById`, `GetVersions`, `Rollback`, `Delete`, `Upsert`). Admin do project A recebe 404 ao enumerar IDs do project B. Ver F5.5 no changelog. |
| `PgAgentVersionRepository`               | 3       | ⚠️ não auditado   | Follow-up. |
| `PgSkillVersionRepository`               | 3       | ⚠️ não auditado   | Follow-up. |
| `PgAgentDefinitionRepository`            | ?       | ✓ via EF          | Herda filter de `AgentDefinitionRow`. |
| `PgWorkflowDefinitionRepository`         | ?       | ✓ via EF          | Herda filter. |
| `PgModelCatalogRepository`               | 4       | N/A               | Catálogo é global por design. |
| `PgDocumentIntelligenceUsageQueries`     | 4       | ⚠️ não auditado   | Follow-up. |
| `PgExecutionAnalyticsRepository`         | 3       | ⚠️ não auditado   | Follow-up. |

---

## Admin-only queries (bypass intencional do filtro)

Queries que agregam dados **cross-project** para painéis admin e por
design não devem filtrar por `ProjectId` corrente:

- `PgLlmTokenUsageRepository.GetAllAgentsSummaryAsync`
- `PgLlmTokenUsageRepository.GetAllWorkflowsSummaryAsync`
- `PgLlmTokenUsageRepository.GetAllProjectsSummaryAsync`
- `PgLlmTokenUsageRepository.GetThroughputAsync`
- `PgAdminAuditLogRepository.QueryAsync` (quando `ProjectId` não é
  passado como filtro)

Autorização dessas queries é feita pelo `DefaultProjectGuard` (só
admin cadastrados em `Admin:AccountIds` podem acessar endpoints que
invocam isso).

---

## Follow-ups (backlog)

Os repos marcados como "⚠️ não auditado" acima ficam como **backlog
TENANCY-SQL-AUDIT** (ver `docs/backlog.md`). A F4 priorizou os que
ficam no hot-path de persona (LlmTokenUsage, PersonaPromptTemplate) e
nos writers de chat (Conversation, NodeExecution via EF).

---

## Como adicionar uma entidade nova com isolamento

1. Domínio (`Core.Abstractions/`) ganha `string ProjectId { get; init; }`.
2. Migration adiciona coluna `ProjectId VARCHAR(128) NULL` + índice
   composto `(ProjectId, ...)`.
3. `AgentFwDbContext.OnModelCreating` registra
   `b.HasQueryFilter(e => e.ProjectId == CurrentProjectId)`.
4. Writer popula `ProjectId = _projectAccessor.Current.ProjectId`.
5. Se houver repo raw-SQL na entidade, adicionar
   `WHERE "ProjectId" = @projectId` nas queries de leitura
   user-scoped; queries admin cross-project ficam sem filter (marcar
   na tabela acima como "admin-only").
