# Runbook — Shared Agents + Pinning

Operação e debug de **agents cross-project** (épico multi-projeto) e **pin
lossless de AgentVersion** (épico Pinning Federated).

## Como desligar a feature

Todas as flags vivem em `appsettings.json` na seção `Sharing` e são lidas via
`IOptionsMonitor<SharingOptions>` — alteração em runtime via reload do config sem restart.

| Cenário | Flag | Efeito |
|---|---|---|
| Desligar tudo (rollback completo) | `Sharing:Enabled=false` | UI esconde toggle de share. `Visibility=global` ignorado em listagens. Audit log preservado. |
| Bloquear cross-project mantendo flag UI | `Sharing:CrossProjectEnabled=false` | `WorkflowValidator` rejeita refs cross-project. Runtime lança `UnauthorizedAccessException`. |
| Ignorar whitelist temporariamente | `Sharing:WhitelistEnabled=false` | `AllowedProjectIds` é ignorado (todo projeto do tenant volta a poder usar agents globais). |
| Reduzir audit pressure | `Sharing:AuditCrossInvoke=false` | Mantém log + métrica, pula gravação em `admin_audit_log`. |

Override via env var:

```
EfsAiHub__Sharing__CrossProjectEnabled=false
```

## Diagnóstico — 5 SQLs úteis

### 1. Listar agents globais e suas whitelists

```sql
SELECT
    "Id",
    "Name",
    "ProjectId" AS owner_project,
    "TenantId",
    "AllowedProjectIds"
FROM aihub.agent_definitions
WHERE "Visibility" = 'global'
ORDER BY "TenantId", "ProjectId";
```

### 2. Detectar agents globais cujo project owner sumiu (orphans)

```sql
SELECT
    a."Id" AS agent_id,
    a."ProjectId" AS missing_project_id,
    a."TenantId",
    a."Name",
    a."UpdatedAt"
FROM aihub.agent_definitions a
LEFT JOIN aihub.projects p ON p.id = a."ProjectId"
WHERE a."Visibility" = 'global'
  AND p.id IS NULL
ORDER BY a."UpdatedAt" DESC;
```

> Mesma lógica que o health check `SharedAgentsHealthCheck` (`/health/ready`).

### 3. Cross-project usage por dia (últimos 7 dias)

```sql
SELECT
    DATE_TRUNC('day', "CreatedAt") AS day,
    "ProjectId" AS caller_project,
    "OriginAgentProjectId" AS owner_project,
    COUNT(*) AS calls,
    SUM("TotalTokens") AS tokens,
    SUM("InputTokens") AS input_tokens,
    SUM("OutputTokens") AS output_tokens
FROM aihub.llm_token_usage
WHERE "OriginAgentProjectId" IS NOT NULL
  AND "CreatedAt" >= NOW() - INTERVAL '7 days'
GROUP BY 1, 2, 3
ORDER BY day DESC, calls DESC;
```

### 4. Whitelist violations (audit log)

```sql
SELECT
    "Timestamp",
    "ProjectId" AS caller_project,
    "ResourceId" AS agent_id,
    "PayloadAfter"
FROM aihub.admin_audit_log
WHERE "Action" = 'cross_project_invoke'
ORDER BY "Timestamp" DESC
LIMIT 100;
```

> Métrica em tempo real: `agents.whitelist_blocked_total{caller_project, owner_project, agent_id}`.

### 5. Cross-project secret resolutions

```sql
-- Métrica OTel: secrets.cross_project_resolutions_total{caller, owner}
-- (sem tabela SQL — exposta apenas via Aspire Dashboard / OTLP)
```

## Diagnóstico — Pinning (3 SQLs úteis)

### P1. Workflows com refs sem pin (defesa)

Pin é mandatório global pós-cleanup pré-prod — esta query deveria retornar 0 linhas.
Resultado positivo indica drift (workflow persistido bypassando o validator).

```sql
-- Conta workflows com AO MENOS 1 agent ref sem pin, por tenant.
SELECT
    "TenantId",
    COUNT(DISTINCT "Id") AS workflows_legacy
FROM aihub.workflow_definitions
WHERE jsonb_path_exists(
    "Data"::jsonb,
    '$.Agents[*] ? (@.AgentVersionId == null || @.AgentVersionId == "")'
)
GROUP BY "TenantId"
ORDER BY workflows_legacy DESC;
```

```sql
-- Detalhe por workflow: lista cada ref sem pin.
SELECT
    wd."Id" AS workflow_id,
    wd."Name",
    wd."TenantId",
    wd."ProjectId",
    agent_ref->>'AgentId' AS agent_id_unpinned
FROM aihub.workflow_definitions wd,
LATERAL jsonb_array_elements(wd."Data"::jsonb -> 'Agents') AS agent_ref
WHERE COALESCE(agent_ref->>'AgentVersionId', '') = ''
ORDER BY wd."TenantId", wd."Id";
```

### P2. AgentVersions sem intent declarado (BreakingChange = NULL)

Versions pré-feature ou criadas via auto-snapshot do `UpsertAsync` sem caller
explicitar intent. Tratadas conservativamente como breaking pelo
`ResolveEffectiveAsync` (não propagam patches).

```sql
SELECT
    av."AgentVersionId",
    av."AgentDefinitionId",
    av."Revision",
    av."CreatedAt",
    av."Status",
    av."SchemaVersion"
FROM aihub.agent_versions av
WHERE av."BreakingChange" IS NULL
  AND av."Status" = 'Published'
ORDER BY av."CreatedAt" DESC
LIMIT 50;
```

> Pra backfill manual: `UPDATE ... SET "BreakingChange" = false WHERE "AgentVersionId" = ?`
> apenas se ops confirmar que a version é não-breaking. Sem confirmação, **não tocar** —
> default conservador é mais seguro.

### P3. AgentVersions orphan (agent_definitions parent sumiu)

Versions cujo agent foi deletado. Workflows pinados nessas versions falham em
runtime (governance hidratada da row corrente exige owner existente).

```sql
SELECT
    av."AgentVersionId",
    av."AgentDefinitionId" AS missing_agent_id,
    av."Revision",
    av."CreatedAt",
    av."Status"
FROM aihub.agent_versions av
LEFT JOIN aihub.agent_definitions ad ON ad."Id" = av."AgentDefinitionId"
WHERE ad."Id" IS NULL
ORDER BY av."CreatedAt" DESC
LIMIT 50;
```

> Mesma lógica que `WorkflowAgentVersionHealthCheck` (`/health/ready`).
> Recuperação: cleanup `DELETE FROM aihub.agent_versions WHERE "AgentDefinitionId" = ?`
> apenas se ops confirmar que workflows pinados nessas versions foram migrados.

## Recuperação — agent orphan

Quando `SharedAgentsHealthCheck` reporta `Degraded` com agents orphan:

1. Identificar via SQL #2.
2. Decidir caso a caso:
   - **Recriar o project owner** — se foi delete acidental, reverter via backup do DB.
   - **Demote o agent pra `project`** — workflows de outros projetos perdem acesso, owner volta. Não há owner real, então essa rota só faz sentido se algum project assumir como dono.
   - **Hard delete o agent** — workflows que o referenciam ficam quebrados. `DELETE FROM aihub.agent_definitions WHERE "Id" = ? AND "Visibility" = 'global';`

## Backfill `tenant_id` em `agent_definitions`

Idempotente — `db/schemas.sql` já roda no startup via `DatabaseBootstrapService`:

```sql
UPDATE aihub.agent_definitions ad
SET "TenantId" = p.tenant_id
FROM aihub.projects p
WHERE ad."ProjectId" = p.id
  AND ad."TenantId" = 'default'
  AND p.tenant_id <> 'default';
```

## Cache invalidation

Cache key tenant-aware: `efs-ai-hub:agent-def:{tenantId}:{id}` em Redis.

Quando admin muda `Visibility` ou `AllowedProjectIds`, o `PgAgentDefinitionRepository.UpsertAsync` faz `RemoveAsync` desse key. Outros projetos do mesmo tenant que tinham cached precisam re-fetch.

Limpeza manual via Redis CLI:
```
DEL efs-ai-hub:agent-def:tenant-X:agent-Y
KEYS efs-ai-hub:agent-def:tenant-X:*
```

## Checklist de deploy

Antes de habilitar `Sharing:Enabled=true` em prod:

- [ ] Backfill de `tenant_id` em `agent_definitions` rodou e completou (verificar SQL #1 — coluna preenchida).
- [ ] Health check `/health/ready` retorna 200 (`shared-agents` healthy).
- [ ] Métrica OTel exposta: `agents.cross_project_invocations_total` aparece no Aspire Dashboard.
- [ ] DBAs cientes do índice parcial `IX_agent_definitions_TenantId_Visibility WHERE Visibility='global'` (criado pelo schema bootstrap).
- [ ] Audit log table tem capacidade pra absorver `cross_project_invoke` (mesmo com throttle de 60s, ambientes com 1000+ workflows ativos podem gerar ~16K rows/dia).

## Pinning v1 — semântica corrente

Pin é **mandatório global** pós-cleanup pré-prod. Não há flag de opt-out:

- Workflow save sem `AgentVersionId` em algum ref → `WorkflowService.ResolveDefaultPinsAsync`
  resolve `current` Published do agent e popula automaticamente. Caller que não declara
  intent recebe pin "current".
- Migration manual via `PATCH /api/workflows/{id}/agents/{agentId}/pin` (audit
  `workflow.agent_version_pinned`).
- Snapshots são **lossless** (não há mais SchemaVersion discriminator). `BreakingChange`
  é `bool` non-nullable (default `false` = patch).

Em deploys que herdam dados pré-cleanup: rodar uma vez o script descartável
`src/EfsAiHub.Migrations.PinningV1/` (deletado do repo após sucesso) que apaga
`agent_versions` legacy + regenera com formato corrente + auto-pina todos os workflows.

## Phase 4 (futuro)

- UI flow de migration: notification bell + modal de diff por agent referenciado.
- `SharedAgentsCoherenceCheck` (workflows referenciando agents inexistentes).
- Audit log partitioning por mês.
- Benchmark formal p99 < 50ms com 1000 agents globais.
