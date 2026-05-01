# Runbook — Shared Agents (Phase 2 + 3)

Operação e debug de **agents cross-project** (épico multi-projeto).

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

## Phase 4 (futuro)

- Pin lossless de versão (snapshot completo em `AgentVersion`).
- `SharedAgentsCoherenceCheck` (workflows referenciando agents inexistentes).
- Audit log partitioning por mês.
- Benchmark formal p99 < 50ms com 1000 agents globais.
