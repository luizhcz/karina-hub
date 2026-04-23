# ADR 003 — ProjectId como boundary de isolamento (multi-tenancy)

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 4

## Context

Pré-F4, o repo tinha dois conceitos de identidade de cliente em paralelo:

1. **`TenantContext`** (populado por `TenantMiddleware` via header `x-efs-tenant-id`). Presente na infra, mas **não** usado em `HasQueryFilter` de nenhuma entidade. `admin_audit_log` guardava `TenantId` só pra analytics.

2. **`ProjectContext`** (populado por `ProjectMiddleware` via header `x-efs-project-id`). Já usado em `HasQueryFilter` de 6 entidades: `ConversationSession`, `WorkflowDefinitionRow`, `AgentDefinitionRow`, `SkillRow`, `WorkflowExecutionRow`, `McpServerRow`.

O plano original da F4 era adicionar um **segundo nível de isolamento** via `TenantId` paralelo, com `HasQueryFilter` composto (`ProjectId AND TenantId`) em 3 tabelas adicionais (`conversations`, `node_executions`, `llm_token_usage`) e scope de persona com prefix `tenant:{tid}:`.

Durante a implementação, descobrimos via inspeção dos dados reais
(`SELECT DISTINCT tenant_id FROM projects`) que:

- `projects.tenant_id` define uma **hierarquia N:1** — um project
  sempre pertence a um único tenant.
- O valor de `admin_audit_log.TenantId` em runtime sempre reflete o
  tenant do project corrente; nunca há dissociação.
- Frontend **não envia** `x-efs-tenant-id` hoje; em dev/prod o
  isolamento efetivo vem do `x-efs-project-id`.

Logo, `ProjectId` já é o boundary natural de isolamento, e adicionar
`TenantId` paralelo seria **defense-in-depth redundante** — +1 coluna,
+1 predicado, +1 write path, para zero ganho de segurança.

## Decision

**ProjectId é o único boundary de isolamento multi-tenant.** TenantId
permanece como **dimensão analítica** (audit + billing cross-project),
não como filtro de query.

### Implicações concretas (F4 revisado)

1. **Nova migration:** `migration_project_id_columns.sql` — adiciona
   coluna `ProjectId VARCHAR(128) NULL` em `node_executions` e
   `llm_token_usage`. `conversations` já tinha.

2. **`HasQueryFilter`** expandido em mais 2 entidades (continuando o
   padrão dos 6 existentes):
   ```csharp
   b.HasQueryFilter(e => e.ProjectId == CurrentProjectId || e.ProjectId == null);
   ```
   `|| e.ProjectId == null` tolera rows legadas pré-F4.

3. **Writers populam `ProjectId`** via `IProjectContextAccessor.Current.ProjectId`:
   - `ConversationService.CreateAsync` (já existia)
   - `TokenTrackingChatClient.TrackUsage` (novo) — lê de
     `ExecutionContext.ProjectId` propagado pelo `WorkflowRunnerService`
     a partir de `execution.Metadata["projectId"]`.

4. **Scope de `PersonaPromptTemplate`** ganha cadeia de 5 níveis
   project-aware:
   ```
   1. project:{projectId}:agent:{agentId}:{userType}  (mais específico)
   2. project:{projectId}:{userType}
   3. agent:{agentId}:{userType}
   4. global:{userType}
   5. null (persona sem bloco)
   ```
   Métodos factory: `ProjectAgentScope`, `ProjectGlobalScope`.

5. **Cache keys** **não** ganham prefix `project:{id}` por default:
   - `CachedPersonaProvider` — persona resolvida não depende de
     project (é dados externos do CRM); 2 projects com mesmo userId
     compartilham cache legitimamente.
   - `PersonaPromptTemplateCache` — chave é o `scope`, que já
     inclui `project:{id}` quando aplicável (natural).

6. **`TenantContext`** fica inalterado em sua forma. `IsSystemContext`
   **não** foi adicionado (seria necessário apenas se houvesse query
   filter de tenant). Cabe revisitar caso `TenantId` venha a ser
   enforced no futuro.

7. **Repos raw-SQL**: inventário em
   [docs/multi-tenant-inventory.md](../multi-tenant-inventory.md).
   Queries user-scoped que retornam linhas do scope atual devem
   adicionar `WHERE "ProjectId" = @projectId` quando a coluna existir.
   Queries admin-only (ex: `GetAllAsync` em pricing) ficam sem filter
   (autorizadas por RBAC).

## Alternatives considered

- **TenantId paralelo** (plano original). Rejeitado: redundante dado
  que `projects.tenant_id` já define a hierarquia.
- **Só TenantId, remover ProjectId filter**. Rejeitado: quebraria 6
  entidades em produção com filter já funcionando.
- **RLS no Postgres**. Backlog separado (ver
  [docs/multi-tenant-rls.md](../multi-tenant-rls.md)). Valor:
  defense-in-depth em caso de bypass do `HasQueryFilter` (ex: query
  raw com conexão não-filtrada).

## Consequences

**Positivo:**
- Código mais simples: um único conceito de isolamento (`ProjectId`).
- Alinhado com o padrão já enforçado — review de novas entidades fica
  "adicionar ProjectId + HasQueryFilter + populate no writer".
- Persona templates ganham scope project-aware sem inventar hierarquia
  nova.

**Negativo:**
- Perde analítica "cross-project within tenant" direta — se surgir,
  precisa JOIN com `projects.tenant_id` em dashboards. Não é hot path.
- Se no futuro quiser recursos compartilhados **entre projects dentro
  do mesmo tenant** (ex: template compartilhado pela organização),
  precisa de um nível extra — mas isso é trabalho "quando surgir",
  não débito atual.

**Follow-ups (backlog):**
- `TENANCY-RLS` — ativar Postgres RLS com `current_setting('app.project_id')`
  como defense-in-depth.
- `TENANCY-SQL-AUDIT` — auditar todos os repos raw-SQL e adicionar
  `WHERE ProjectId` onde aplicável. F4 cobriu os críticos; resto fica
  em `docs/multi-tenant-inventory.md`.

## References

- `src/EfsAiHub.Core.Abstractions/Identity/TenantContext.cs`
- `src/EfsAiHub.Core.Abstractions/Identity/IProjectContextAccessor.cs`
- `src/EfsAiHub.Infra.Persistence/DbContext/AgentFwDbContext.cs` (query filters)
- `db/migration_project_id_columns.sql`
- `docs/multi-tenant-inventory.md`
