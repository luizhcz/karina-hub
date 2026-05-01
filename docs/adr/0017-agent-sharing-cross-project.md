# ADR 0017 — Agent Sharing Cross-Project (Phase 2)

**Status:** Aceito
**Data:** 2026-05-01
**Contexto:** Fase 2 do épico "Workflows multi-projeto (federated agents)"

## Contexto

Phase 1 (ADR 0016) habilitou `WorkflowDefinition.Visibility="global"` com tenant boundary. Phase 2 estende o mesmo padrão a `AgentDefinition` — workflow do projeto A pode referenciar agent global do projeto B no mesmo tenant.

Comportamento anterior:
- `AgentDefinitionRow` filtrado estritamente por `ProjectId == CurrentProjectId`.
- `WorkflowValidator.ValidateAgentReferencesAsync` rejeitava qualquer agent ref que não fosse do projeto atual.
- `AgentFactory.CreateAgentsForWorkflowAsync` resolvia agents apenas do projeto local.
- `LlmTokenUsageRow` registrava só `ProjectId` (caller); sem analytics dual.

## Decisão

`AgentDefinition.Visibility ∈ {"project","global"}` com tenant boundary estrito + camadas adicionais críticas que evitam vazamento de credenciais e de dados cross-tenant.

### Modelo de dados

- `AgentDefinition` ganha `Visibility` (set, default "project") e `TenantId` (denormalizado de `projects.tenant_id`).
- `AgentDefinitionRow.HasQueryFilter` estendido pra `(ProjectId == CurrentProjectId) OR (Visibility == "global" AND TenantId == CurrentTenantId)` — workflow do projeto A vê agents globais do mesmo tenant automaticamente sem bypass.
- `LlmTokenUsageRow` ganha `OriginAgentProjectId VARCHAR(128) NULL`. Quando workflow caller != owner do agent global, registra owner aqui; senão null. Index composto `(ProjectId, OriginAgentProjectId, CreatedAt)` cobre analytics dual.
- DDL idempotente em `db/schemas.sql`: `ADD COLUMN IF NOT EXISTS` com DEFAULT (metadata-only PG 11+), CHECK `NOT VALID + VALIDATE`, índice parcial `WHERE Visibility='global'`, backfill via JOIN com `projects`.

### API

- `PATCH /api/agents/{id}/visibility` body `{visibility, reason?}` — endpoint dedicado com audit `agent.visibility_changed`.
- `CreateAgentRequest.Visibility?` opcional (default "project" em Create; preserved em Update).
- `AgentResponse` expõe `visibility`, `originProjectId`, `originTenantId`.
- `AgentService.UpdateAsync` preserva `existing.Visibility/ProjectId/TenantId` (PUT sem campo não reseta).

### Owner gate

Mesmo padrão de Phase 1 — somente requests no `ProjectId` dono podem mudar visibility. Caller de outro projeto recebe `403`. Mensagem genérica (não vaza ProjectId do owner — defesa contra info-leak).

### Cache tenant-aware

Cache key migrada de `agent-def:{id}` para `agent-def:{tenantId}:{id}`. Mesmo se algum caminho bypass query filter, cache não vaza cross-tenant. Implementado em `PgAgentDefinitionRepository.CacheKey` injetando `ITenantContextAccessor`.

### Hidratação consistente

`PgAgentDefinitionRepository.Hydrate(row, def)` força `def.ProjectId/TenantId/Visibility = row.*` em **todos** os paths (`GetByIdAsync`, `GetAllAsync`). Defesa contra JSON pré-Phase 2 ou divergência transient. `GetByIdAsync` usa `FirstOrDefaultAsync` (respeita query filter), nunca `FindAsync` (que bypassa).

### Cross-project resolution — fixes críticos

#### Credential isolation (já correto pré-Phase 2)

`AgentFactory.InjectProjectCredentials` lê `_projectRepo.GetByIdAsync(definition.ProjectId)` — `definition.ProjectId` é o owner (após Hydrate). Caller nunca vê secrets do owner. **Pré-existente, validado em Phase 2.**

#### Skill resolution

`ISkillRepository.GetByIdForOwnerAsync(id, ownerProjectId, ct)` — bypass de query filter restrito ao owner project. `ISkillResolver.ResolveAsync` ganha overload `ownerProjectId?`; quando setado E ref não tem `SkillVersionId` (current version), usa `GetByIdForOwnerAsync`. `AgentFactory.ResolveSkills` calcula `ownerProjectId = definition.ProjectId` quando caller != owner, propaga.

> Versões pinadas (`SkillVersionId` setado) já funcionam cross-project nativamente — `SkillVersionRow` é append-only e não tem `HasQueryFilter`.

#### MCP resolution

Diferido pra Phase 3. MCP cross-project é caso de uso menos comum (clientes geralmente têm seus próprios MCP servers locais). Quando agent global referencia MCP local do owner, hoje a resolução falha silenciosamente (provider loga warning e pula a tool). Phase 3 implementa overload `IMcpServerRepository.GetByIdForOwnerAsync` no mesmo padrão das skills.

### Billing dual

`TokenTrackingChatClient` recebe `agentOwnerProjectId` no construtor (passado pelo `AgentFactory`). Em `TrackUsage`:

- `LlmTokenUsage.ProjectId = ctx?.ProjectId` (caller — quem paga)
- `LlmTokenUsage.OriginAgentProjectId = agentOwnerProjectId` se diferente do caller; senão null

Caller paga; owner aparece em analytics. Evita confusão de cobrança e mantém auditoria consistente.

### Audit + métricas

- `AdminAuditActions.AgentVisibilityChanged` ("agent.visibility_changed") — payloads mínimos (`{visibility}` antes/depois + `reason?`).
- `AdminAuditActions.CrossProjectInvoke` ("cross_project_invoke") — emitido em `AgentFactory.CreateAgentsForWorkflowAsync` toda vez que workflow.ProjectId != agent.ProjectId. Payload: `{callerProjectId, ownerProjectId, workflowId, agentId}`. **Phase 3 adiciona throttle (LRU 60s)** pra evitar inflar audit em workloads alto.
- Métricas OTel novas:
  - `agents.visibility_changes_total{from, to, tenant}`
  - `agents.cross_project_invocations_total{caller_project, owner_project, tenant}`

## Alternativas rejeitadas

1. **Cross-tenant sharing** — viola tenant boundary; rejeitado.
2. **AdminGate pra trocar Visibility** — alinhado a Phase 1 (qualquer usuário do projeto dono).
3. **Whitelist explícita por agent (`AllowedProjectIds[]`)** — defer pra Phase 3.
4. **Pin obrigatório de versão** — Phase 2 mantém versão flutuante; Phase 3 adiciona `WorkflowAgentReference.AgentVersionId?` opcional.
5. **Cache key sem tenant** — vazamento latente; rejeitado.
6. **`IgnoreQueryFilters` em `GetByIdAsync`** — info-leak cross-tenant; usar `FirstOrDefaultAsync` que respeita filter.

## Consequências

- DBs antigos precisam de backfill de `tenant_id` em `agent_definitions`. `DatabaseBootstrapService` re-roda DDL idempotente no startup.
- Agents JSON antigos sem `Visibility` defaultam a `"project"` (BC preservada via `init` default; serialização tolera ausência).
- Workflow snapshots históricos referenciando agent que depois ficou cross-project continuam válidos — `Hydrate` garante consistência ao deserializar.
- Cross-project secret/skill/MCP precisa de owner project ainda existente. Se owner project for deletado, agent global vira "orphan" — Phase 3 adiciona health check `SharedAgentsHealthCheck`.

## Migration safety

- `ALTER TABLE ... ADD COLUMN ... DEFAULT 'default'` é metadata-only no PG 11+ (sem rewrite).
- CHECK constraint via `NOT VALID + VALIDATE` em transações separadas — sem lock exclusivo > 100ms.
- Index parcial criado com `IF NOT EXISTS`.
- `OriginAgentProjectId` em `llm_token_usage`: ADD COLUMN nullable + index composto não bloqueia escritas.

## Phase 3 (próxima)

- Whitelist `AllowedProjectIds[]?` em `AgentDefinition`.
- Pin opcional de versão em `WorkflowAgentReference.AgentVersionId?`.
- MCP cross-project resolution (`IMcpServerRepository.GetByIdForOwnerAsync`).
- `SecretContext.OriginProjectId` + cache segregado em `SecretCacheService`.
- Health check `SharedAgentsHealthCheck` (orphans).
- Throttle LRU em audit `cross_project_invoke`.
- Feature flags `Sharing:*` via `IOptionsMonitor` pra rollback graceful.
- Performance benchmark `ListVisibleAsync` p99 < 50ms com 1000 agents globais.
