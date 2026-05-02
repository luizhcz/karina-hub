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
| Desligar pin obrigatório (rollback Pinning) | `Sharing:MandatoryPin=false` | `WorkflowValidator` aceita workflow sem `AgentVersionId`. Auto-pin lazy desligado no `AgentFactory`. Pins existentes continuam respeitados. |
| Rollout staged por tenant | `Sharing:MandatoryPinTenants=["tenant-A"]` | Combina com `MandatoryPin=true`: enforcement APENAS pra tenants listados. null/vazia + flag on = global. |
| Kill switch lossless | `Sharing:LosslessAgentVersion=false` | `AgentFactory` ignora `SchemaVersion=2` snapshots e cai sempre no path legacy (live definition). Métrica `strategy=legacy_fallback`. |

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

### P1. Workflows legados sem pin (por tenant)

Identifica workflows que ainda têm refs sem `AgentVersionId` — alvos de auto-pin
quando `Sharing:MandatoryPin=true` for habilitado pro tenant.

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

## Playbook — MandatoryPin rollout (épico Pinning Federated)

Rollout incremental de pin obrigatório por tenant. Default `MandatoryPin=false`
preserva BC; ativar só após validação.

### Etapa 1 — Inventário

Antes de habilitar pra qualquer tenant, rodar SQL P1 pra contar workflows legados
(refs sem pin). Esses serão alvo do auto-pin lazy no first execute pós-flag.

### Etapa 2 — Tenant piloto

```json
{
  "Sharing": {
    "MandatoryPin": true,
    "MandatoryPinTenants": ["tenant-piloto"]
  }
}
```

`IOptionsMonitor` recarrega em runtime. Efeitos:
- Workflows novos em `tenant-piloto` SEM pin → 400 com mensagem clara.
- Workflows existentes em `tenant-piloto` SEM pin → auto-pinados no first
  AgentFactory call. Audit `workflow.agent_version_auto_pinned` emitido.
- Outros tenants: comportamento legacy preservado (pin opcional).

### Etapa 3 — Verificação

```bash
# Health check
curl http://localhost:5189/health/ready | jq '.entries["agent-version-orphans"].data'

# Métrica OTel: workflows.agent_version_auto_pin_total
# Spike no início do rollout → estabiliza após convergência.

# SQL P1 pra confirmar redução de workflows legacy no tenant piloto.
```

### Etapa 4 — Expansão

Adicionar tenants ao whitelist gradualmente:

```json
{
  "Sharing": {
    "MandatoryPin": true,
    "MandatoryPinTenants": ["tenant-piloto", "tenant-A", "tenant-B"]
  }
}
```

Quando enforcement estiver universal, simplificar:

```json
{
  "Sharing": {
    "MandatoryPin": true,
    "MandatoryPinTenants": null
  }
}
```

`null` ou lista vazia + flag on = enforcement GLOBAL.

### Etapa 5 — Rollback (se necessário)

Setar `Sharing:MandatoryPin=false`. Em runtime, sem restart. Pins existentes
permanecem (não são revertidos). Workflows futuros podem voltar a salvar sem pin.

### Kill switch lossless

Se snapshot v2 apresentar regressão:

```json
{
  "Sharing": {
    "LosslessAgentVersion": false
  }
}
```

`AgentFactory` ignora SchemaVersion=2 e cai sempre no path legacy (live
definition). Métrica `agents.version_pin_resolutions_total{strategy=legacy_fallback}`
sinaliza ativação.

## Phase 4 (futuro)

- UI flow de migration: notification bell + modal de diff por agent referenciado.
- `SharedAgentsCoherenceCheck` (workflows referenciando agents inexistentes).
- Audit log partitioning por mês.
- Benchmark formal p99 < 50ms com 1000 agents globais.
