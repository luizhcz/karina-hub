# Multi-Tenant — Postgres RLS (Backlog)

Status: **não executado.** Documentado aqui como plano pra quando
surgir necessidade.

## Motivação

Hoje o isolamento multi-tenant é enforçado em **camada de aplicação**
via `HasQueryFilter<AgentFwDbContext>` (ver
[ADR 003](adr/003-project-as-tenancy-boundary.md)). Isso é suficiente
enquanto 100% das queries passam pelo EF Core ou pelos repos raw-SQL
auditados.

RLS (Row-Level Security) no Postgres adiciona **defense-in-depth**:
mesmo uma conexão direta via `psql` ou uma query raw esquecida respeita
o boundary.

## Plano

### Passo 1 — Configurar variável de sessão

Cada request autenticada faz:

```sql
SET LOCAL app.project_id = '<projectId>';
```

Isso é injetado via `DbContext.OnConfiguring` com um
`SetCommand(Npgsql)` ou via middleware que roda antes de qualquer
query.

### Passo 2 — Ativar RLS nas tabelas filtradas

Pra cada entidade com `HasQueryFilter` por `ProjectId`:

```sql
ALTER TABLE aihub.conversations ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.workflow_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.agent_definitions ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.skills ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.workflow_executions ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.mcp_servers ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.node_executions ENABLE ROW LEVEL SECURITY;
ALTER TABLE aihub.llm_token_usage ENABLE ROW LEVEL SECURITY;
```

### Passo 3 — Criar policies

```sql
CREATE POLICY project_isolation_read ON aihub.conversations
    FOR SELECT
    USING ("ProjectId" = current_setting('app.project_id', true)
           OR "ProjectId" IS NULL);

CREATE POLICY project_isolation_write ON aihub.conversations
    FOR INSERT
    WITH CHECK ("ProjectId" = current_setting('app.project_id', true));

-- Repetir pras demais tabelas.
```

### Passo 4 — Roles separados

Roles de app **não** devem ter `BYPASSRLS`. Só o role de migration
tool (usado pelo `apply.sh`, jobs de retention) mantém bypass.

```sql
CREATE ROLE efs_app NOINHERIT LOGIN;
ALTER ROLE efs_app NOBYPASSRLS;

CREATE ROLE efs_system BYPASSRLS LOGIN;
-- efs_system usado por BackgroundService que precisa ver todos os projects
-- (ex: LlmCostRefreshService, AuditRetentionService).
```

Connection string do Npgsql (`ConnectionStrings:AgentFw`) passa a usar
`efs_app`; jobs dedicados usam `efs_system`.

### Passo 5 — Testes

- Unit test que abre conexão como `efs_app` sem `SET app.project_id`
  → nenhuma row retornada de `conversations`.
- Unit test que faz `SET LOCAL app.project_id='A'` + query em dados
  de project B → 0 rows.
- Integration test com 2 tenants distintos em paralelo.

## Gatilhos pra executar

- Requisito explícito de SOC2/ISO27001 que audita defense-in-depth.
- Incidente de vazamento entre projects via bypass de filter.
- Introdução de clientes com dados regulados (LGPD sensível,
  HIPAA-equivalent) que precisam de isolamento físico enforcado em DB.

## Esforço estimado

- Implementação: 1d (policies + roles + conn string + sessão).
- Testes: 1d.
- Ajuste de operações (cuidado: `SET LOCAL` precisa vir em CADA
  transação, nem sempre trivial em EF ChangeTracker pooled context).
- Total: 2-3d.
