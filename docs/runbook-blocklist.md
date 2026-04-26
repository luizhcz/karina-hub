# Runbook — Blocklist Guardrail

Guia operacional para o time de oncall investigar incidentes envolvendo o middleware
de blocklist (`BlocklistChatClient`). Atualizado em 2026-04-25 (v1).

## TL;DR — Status codes esperados

| Código | Cenário | Envelope HTTP |
|---|---|---|
| **422** | Conteúdo bloqueado por política do projeto | `{error: "policy_violation", category, violation_id, retryable: false, message}` |
| **500** | Falha interna — ver seções abaixo | `{error: "Internal server error"}` (genérico) |
| **503** | Catálogo indisponível (DB/Redis down) — engine usa cache antigo, não emite 503 hoje | n/a |

## 422 não é erro. É comportamento esperado.

Quando uma violação de política bate, o middleware retorna **HTTP 422 com envelope estruturado**. Isso é **comportamento desejado** — não escalar como bug sem antes:

1. Conferir `violation_id` no envelope HTTP retornado.
2. Buscar no `admin_audit_log` (latência: ~1s pós-violação; retenção: padrão da tabela, sem TTL específico):
   ```sql
   SELECT "Id", "Timestamp", "ProjectId", "ResourceId" AS agent_id,
          "PayloadAfter"->>'phase' AS phase,
          "PayloadAfter"->>'category' AS category,
          "PayloadAfter"->>'pattern_id' AS pattern_id,
          "PayloadAfter"->>'action_taken' AS action,
          "PayloadAfter"->>'context_obfuscated' AS context
   FROM aihub.admin_audit_log
   WHERE "Action" = 'blocklist_violation'
     AND "PayloadAfter"->>'violation_id' = '<uuid>';
   ```
3. **Se a query retornar 0 rows** dentro de ~5s da violação, há 2 possibilidades:
   - Audit log gravou mas com payload diferente (improvável — schema do payload é fixo no `BlocklistChatClient.EmitMetricAndAuditAsync`).
   - `ProjectSettings.Blocklist.AuditBlocks=false` no projeto (admin desabilitou o audit). Confirmar via `GET /api/projects/{id}/blocklist`.
4. Se categoria + `pattern_id` fazem sentido pro projeto, é **funcionamento correto**. Se não, é falso positivo — abrir issue de catálogo (`#efs-compliance`).

## 500 com `error: "Internal server error"` em rota com blocklist ativo

> **Trade-off consciente do v1**: o envelope HTTP é genérico **deliberadamente**. Mensagem
> específica seria útil pro debug do oncall, mas vazaria implementação interna do middleware
> pra o cliente final. v2 (no backlog) retorna envelope estruturado tipo
> `{error: "policy_misconfigured", details}` apenas pra requests com header de admin.
> Por enquanto: **mensagem genérica pro cliente + log estruturado server-side**.

O envelope genérico não vaza implementação, mas o **log captura o detalhe**. Causas conhecidas:

### Causa 1 — `ProjectId` ausente no `ExecutionContext`

**Sintoma**: 500 Internal Server Error em request normal de chat ou agent session.

**Comportamento esperado v1**: o cliente recebe envelope genérico (não vaza
"middleware blocklist falhou"), mas o log do servidor mostra erro estruturado.

**Como diagnosticar** (strings exatas validadas contra `src/EfsAiHub.Platform.Runtime/Guards/BlocklistChatClient.cs:258` e `src/EfsAiHub.Platform.Runtime/Application/Services/AgentSessionService.cs:192`):

```bash
# Caminho 1: BlocklistChatClient direto (workflow runner ou outro caller)
grep "[BlocklistChatClient] ProjectId ausente em ExecutionContext" /var/log/efs-ai-hub/api.log

# Caminho 2: AgentSessionService (multi-turn standalone)
grep "ProjectContext não foi populado pelo ProjectMiddleware" /var/log/efs-ai-hub/api.log
```

Se nenhum dos dois aparecer mas o 500 persiste, **NÃO é causa de blocklist** — investigar
outras camadas (TokenTracking, Circuit, provider LLM).

**Causas raízes possíveis**:

- `ProjectMiddleware` não rodou (caminho não-HTTP, ex: scheduled job recém-introduzido sem propagar projectId).
- `WorkflowExecution.Metadata["projectId"]` ausente em execuções legadas (pré PR 1).
- `IProjectContextAccessor` não populado em código novo que não usa pipeline HTTP padrão.

**Ações corretivas (oncall)**:

1. Identificar o caller pelo stack trace no log de erro (linha do throw em `AgentSessionService.cs:192` ou `BlocklistChatClient.cs:258`).
2. Verificar se cliente está enviando header `x-efs-project-id` ou JWT claim `project_id`.
   - **Sim e funciona pra outras requests**: 1 request específica com problema → coletar request-id e abrir issue em `#efs-platform` com payload + headers.
   - **Não envia mas era pra estar enviando**: alinhar com cliente sobre fix no integrador.
3. Se for caminho não-HTTP recém-introduzido (job, scheduler, listener): autor do código novo precisa popular `IProjectContextAccessor.Current` ou `WorkflowExecution.Metadata["projectId"]`. **Não é fix de runtime — é fix de código + redeploy.**
4. **Não tente debug de async/AsyncLocal localmente.** Se `IsExplicit=false` aparecer em request HTTP que enviou header correto, é bug arquitetural — abrir issue com stack trace completo e correlationId em `#efs-platform`. Não é resolvível em ops.
5. Workaround temporário: NÃO existe via config. Setar `Admin:AccountIds` vazio (desabilita AdminGate) NÃO resolve — AdminGate é diferente do ProjectMiddleware.

### Causa 2 — `IWorkflowEventBus` não registrado no DI

**Sintoma**: app não sobe. Log critical:
```
[BlocklistEngine] IWorkflowEventBus não registrado no DI. SAFETY_VIOLATION nunca seria emitido em SSE — cliente veria HTTP 422 mas sem evento terminal. Registre o messaging (PgEventBus) antes do BlocklistEngine no Program.cs.
```

**Ação**: garantir ordem em `Program.cs` — `AddMessaging()` antes de `AddBlocklist()` ou
similar. Sem isso, refusa subir (fail-fast).

### Causa 3 — Catálogo malformado no Redis L2

**Sintoma**: ocasional 500 em chat. Logs:
```
[BlocklistEngine] L2 cache do catálogo malformado — refazendo fetch.
```

**Ação**: nenhuma — engine refaz fetch automaticamente. Se persistir, limpar manualmente:
```bash
redis-cli DEL "blocklist:catalog"
```

## SAFETY_VIOLATION em chat: cliente vê 422 mas sem evento terminal SSE

**Causa**: backend bloqueou em **input** (ChatRole.User scan) — middleware retorna 422 antes
de qualquer SSE iniciar. **Não há evento terminal** porque stream nem abriu. Cliente deve
exibir o envelope 422 como erro normal.

Se o bloqueio aconteceu em **output** durante streaming, o evento `SAFETY_VIOLATION` chega
via SSE como terminal event antes do 422 ser convertido pelo handler de exception. Frontend
deve handlear ambos.

## Métricas para acompanhar

Prometheus/OpenTelemetry — meter `EfsAiHub.Api`:

| Counter | Tags | Quando alarmar |
|---|---|---|
| `blocklist.violations` | phase, category, action | Spike repentino → mudança de comportamento do LLM ou pattern novo capturando demais |
| `blocklist.scans` | phase | Sem tráfego = blocklist desligado em todos projetos (verificar) |
| `blocklist.load_errors` | — | Qualquer valor > 0 / 5min → DB ou Redis com problema |
| `blocklist.cache.hits` | layer (l1/l2) | l1 hit ratio < 90% → TTL muito agressivo |

## Catálogo: como atualizar

DBA edita `db/seeds.sql` e roda `db/apply.sh` no Postgres da produção. Trigger
`pg_notify('blocklist_changed')` propaga em <2s para todos os pods. Não requer redeploy
do app.

## Killswitch granular por projeto

Se um pattern do catálogo dá falso positivo num projeto específico:

```sql
-- Editar via UI admin (frontend) ou direto:
UPDATE aihub.projects
SET settings = jsonb_set(
    settings,
    '{Blocklist,Groups,PII,DisabledPatterns}',
    '["pii.cpf"]'::jsonb)
WHERE id = '<project-id>';
```

Próxima request rebuild matcher sem o pattern desabilitado. Catálogo geral fica intacto.

## Killswitch global (emergência)

Desligar blocklist completamente para um projeto (mantém catálogo intacto):

```sql
UPDATE aihub.projects
SET settings = jsonb_set(settings, '{Blocklist,Enabled}', 'false'::jsonb)
WHERE id = '<project-id>';
```

Para desligar globalmente em todos os projetos, marcar todos os patterns do catálogo
como `Enabled=false` (NÃO recomendado — perde audit trail):
```sql
UPDATE aihub.blocklist_patterns SET "Enabled" = FALSE;
```

## Limitações conhecidas (v1)

1. **Streaming output sliding-window de 128 chars**: padrões >128 chars podem ter prefixo
   emitido antes do block. Patterns curados (CPF/CNPJ/JWT/AWS) são <100 chars. Se cliente
   reclama de vazamento parcial, consultar tamanho do pattern.

2. **Caminho não-HTTP sem ProjectId**: AgentSession via API direta sem pipeline HTTP
   adequado lança InvalidOperationException → cliente recebe 500 genérico (envelope não
   vaza implementação). Documentado como design choice v1; refactor pra mensagem específica
   em backlog.

3. **Performance O(N*M) do matcher**: a cada update no streaming, re-scan do buffer
   completo. Pra 100KB de resposta com 50 patterns = ~100M regex ops acumuladas. Aceitável
   pra patterns atuais (<500 chars). Otimização (alternation única) está no backlog v2 com
   gate por telemetria.

## Contatos

- Plataforma backend: `#efs-platform`
- DBA / catálogo: `#efs-dba`
- Compliance / decisão de pattern novo: `#efs-compliance`
