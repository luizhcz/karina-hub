# Backlog — EfsAiHub

Itens gerados por decisões arquiteturais (ADRs) e reviews ao longo da
implementação. Cada entrada tem um ID estável, um gatilho concreto de
re-avaliação e referência ao contexto onde foi decidida.

---

## Persona — Observabilidade & SDK

### PERSONA-OBS-1 — `prompt_cache_key` quando OpenAI SDK suportar

**Origem:** [ADR 000](adr/000-opensdk-shape.md) — Decisão 2.

**Contexto:** O parâmetro `prompt_cache_key` do OpenAI (2025) não está
suportado em `OpenAI` SDK 2.10.0 — issue oficial
[openai/openai-dotnet#641](https://github.com/openai/openai-dotnet/issues/641)
marcada como "blocked: spec". Hoje usamos prefixo invariante no system
message como mitigação best-effort.

**Trabalho:** quando o SDK expuser `prompt_cache_key` nativamente,
popular o campo em `ChatOptions` no `ChatOptionsBuilder.BuildCoreOptions`
com valor estável `hash(tenantId + agentId)` pra forçar stickiness de
shard.

**Gatilho:** release do `OpenAI` SDK que feche o issue #641 (esperado
2.11.x ou 3.x). Monitorar changelog.

**Esforço estimado:** 2h (spike) + 2h (implementação).

---

### PERSONA-OBS-2 — Avaliar suporte a `user` param (abuse tracking)

**Origem:** [ADR 000](adr/000-opensdk-shape.md) — mencionado em follow-ups.

**Contexto:** OpenAI aceita `user` param no body (`hash(userId)` recomendado
pra abuse-tracking). Tipo `ChatCompletionOptions.EndUserId` existe em
`OpenAI` SDK 2.10.0, mas o adapter `Microsoft.Extensions.AI.OpenAI`
10.4.0 não expõe rota pra setá-lo via `ChatOptions`.

**Trabalho:** investigar se vale criar um decorator custom sobre
`IChatClient` que injete `EndUserId` no `ChatCompletionOptions` antes
de enviar. ADR próprio com decisão.

**Gatilho:** a qualquer momento — requisito de compliance/abuse-tracking
pode tornar prioritário.

**Esforço estimado:** 1d.

---

### PERSONA-OBS-3 — Testes unitários de `ActivityExtensions.SetPersonaTags`

**Origem:** Review F1.

**Contexto:** Helper novo em `src/EfsAiHub.Infra.Observability/Tracing/
ActivityExtensions.cs`. Testável via `ActivityListener` in-memory;
cobre early-return em null/Anonymous, determinismo do hash, tags por
subtipo (ClientPersona vs AdminPersona), garantia de não vazar PII
(ClientName/Username/etc não aparecem como tag).

**Esforço estimado:** 2h.

---

### PERSONA-OBS-4 — Semântica de `ResourceId="*"` em read audit de collection

**Origem:** Review F1.

**Contexto:** `PersonaPromptTemplatesAdminController.GetAll` grava
`admin_audit_log` com `ResourceId = "*"`. Dashboards que filtram por
ID específico vão misturar esses eventos. Alternativas: `ResourceType
= persona_prompt_template_collection` ou `ResourceId = ""`.

**Esforço estimado:** 30min + migration de dados históricos se quiser
consistência retroativa.

---

### CACHE-INV-1 — Métricas de invalidação cross-pod

**Origem:** Review F2 (N1).

**Contexto:** `ICacheInvalidationBus` não emite métricas. Sem
`cache.invalidation.published/received/echo_filtered`, troubleshoot de
stale cross-pod em produção depende só de log scraping.

**Trabalho:** registrar 3 counters em `MetricsRegistry` e emitir no
`PgCacheInvalidationBus` (publish) + no handler wrapper (received/
echo_filtered).

**Esforço estimado:** 1h.

---

### CACHE-INV-2 — Integration test cross-pod real

**Origem:** Review F2 (N2).

**Contexto:** Hoje o smoke F2 depende de `pg_notify` manual em execução
humana. Integration test com `Testcontainers.PostgreSQL` + 2 instâncias
do `PgCacheInvalidationBus` (com `SourcePodId` diferente via ctor
parametrizável) simularia dois pods no mesmo processo.

**Esforço estimado:** 3h.

---

### TENANCY-STRICT-FILTER — Remover tolerância a `ProjectId == null`

**Origem:** Review F4 (ressalva obrigatória atenuada).

**Contexto:** `HasQueryFilter` em `NodeExecutionRow` e `LlmTokenUsageRow`
hoje inclui `|| e.ProjectId == null` para tolerar rows legadas pré-F4.
Efeito colateral: callers que omitem `metadata["projectId"]` também
geram rows null que viram visíveis cross-project. Hoje mitigado
por log warning no `WorkflowRunnerService` + writer que popula via
`DelegateExecutor`. Plano: backfill das rows antigas + enforcement
estrito no trigger + remover a cláusula null.

**Passos:**
1. Script de backfill: `UPDATE llm_token_usage SET "ProjectId" = 'default' WHERE "ProjectId" IS NULL;` (e mesmo para node_executions), executado em janela de manutenção.
2. Adicionar validação no `WorkflowsController.Trigger`/`Sandbox` rejeitando request sem `projectId`.
3. Remover `|| e.ProjectId == null` dos `HasQueryFilter`.
4. Tornar coluna `ProjectId` `NOT NULL` com `DEFAULT 'default'`.

**Gatilho:** após o warning log parar de aparecer na telemetria (indica que ninguém está mais gerando rows null).

**Esforço estimado:** 4h incluindo migration de backfill.

---

### TENANCY-RLS — Ativar Postgres Row-Level Security como defense-in-depth

**Origem:** [ADR 003](adr/003-project-as-tenancy-boundary.md) + plano F4.

**Contexto:** Hoje isolamento multi-tenant é enforçado em camada de
aplicação via `HasQueryFilter` (EF) + `WHERE ProjectId` nos repos
raw-SQL auditados. Se alguma query esquecer o filter ou uma conexão
direta for aberta, o boundary é quebrado. RLS no Postgres (policies
`USING (ProjectId = current_setting('app.project_id', true))`) trava
isso no motor do DB.

**Trabalho:** ver [docs/multi-tenant-rls.md](multi-tenant-rls.md).

**Gatilho:** requisito SOC2/ISO27001, ou primeiro incidente de
vazamento cross-project em audit.

**Esforço estimado:** 2-3d.

---

### TENANCY-SQL-AUDIT — Completar auditoria dos repos raw-SQL

**Origem:** F4 ([docs/multi-tenant-inventory.md](multi-tenant-inventory.md)).

**Contexto:** F4 auditou repos críticos (LlmTokenUsage, Conversation,
NodeExecution, PersonaPromptTemplate). Outros (`PgBackgroundResponseRepository`,
`PgAgentVersionRepository`, `PgSkillVersionRepository`,
`PgExecutionAnalyticsRepository`, `PgDocumentIntelligenceUsageQueries`)
não foram auditados pro escopo não estourar.

**Trabalho:** revisar cada um; queries de leitura user-scoped ganham
`WHERE "ProjectId" = @projectId`; admin-only documentadas explicitamente.

**Esforço estimado:** 1d.

---

### TENANCY-ISOLATION-TESTS — Integration test cross-project

**Origem:** F4 (planejado, não executado).

**Contexto:** Não há hoje teste automatizado que garanta isolamento
cross-project (ex: ProjectA cria conversa, ProjectB lê e confirma que
não vê). Incidente de vazamento passaria no CI.

**Trabalho:** em `tests/EfsAiHub.Tests.Integration/`, spinar
`WebApplicationFactory` com 2 headers diferentes + asserts cross-read.
Fixture precisa aceitar override de `x-efs-project-id`.

**Esforço estimado:** 3h.

---

### PERSONA-VER-1 — Rejeitar rollback pra ActiveVersionId corrente

**Origem:** Review F5 (nice-to-have).

**Contexto:** `RollbackAsync` hoje aceita rollback pra version que já
é ativa e cria nova version duplicada (polui histórico). UI já esconde
o botão; só reproduzível via API direta.

**Trabalho:** `RollbackAsync` devolve null ou erro específico quando
`targetVersionId == template.ActiveVersionId`. Controller traduz em
400 com mensagem clara.

**Esforço estimado:** 15min.

---

### PERSONA-VER-2 — Padronizar `/rollback` para body JSON + ChangeReason custom

**Origem:** Review F5 (nice-to-have — unifica 2 itens).

**Contexto:** Hoje o endpoint usa querystring `?versionId=...`,
inconsistente com `AgentsController` que usa body `[FromBody]
RollbackAgentRequest`. Além disso, o `ChangeReason` é hardcoded como
`"rollback to {versionId}"` — admin não pode passar "rollback por
regressão X em produção".

**Trabalho:** criar DTO `PersonaPromptTemplateRollbackRequest` com
`VersionId Guid` + `ChangeReason string?`. Controller aceita no body
e propaga o reason pro repository (que já tem o parâmetro).

**Esforço estimado:** 30min + update no frontend (`rollbackPersonaTemplate`).

---

### PERSONA-VER-3 — Adicionar `AdminAuditActions.Rollback`

**Origem:** Review F5 (nice-to-have).

**Contexto:** Hoje o rollback grava `action='update'` no audit log com
ResourceId composto `{id}:rollback:{versionId}`. Filtragem por tipo
de ação (dashboard "quantos rollbacks por semana") exige LIKE na string.

**Trabalho:** adicionar constante `AdminAuditActions.Rollback = "rollback"`
e trocar o `Build(...)` do endpoint pra usar. Update em dashboards
que agregam por Action (nenhum hoje).

**Esforço estimado:** 10min.

---

### PERSONA-VER-RETENTION — Policy de retenção para versions antigas

**Origem:** [ADR 004](adr/004-persona-template-versioning.md) — seção
Negative consequences.

**Contexto:** `persona_prompt_template_versions` é append-only; cada
edit + rollback gera linha. Em tenant com churn alto de templates, a
tabela cresce indefinidamente. Hoje N templates × M edits = NM rows.

**Trabalho:** policy configurável (ex: manter últimas 100 versions
por template, compactar o resto em resumo). Job de retention no Host.Worker.

**Gatilho:** alguma consulta em dashboard admin ficar lenta ou
tamanho da tabela passar de X GB.

**Esforço estimado:** 1d.

---

### HOUSEKEEPING-1 — Injetar ILogger em `UserPersonaFactory.Anonymous`

**Origem:** Review F3 (N1).

**Contexto:** Hoje o fallback silencioso só emite `Activity.AddEvent` —
se o caller não está sob span (background worker, startup), o evento
vira no-op. Um typo em config carregada no boot passa despercebido.

**Trabalho:** static delegate configurável ao registrar DI (`UserPersonaFactory.OnUnknownUserType = logger.LogWarning`) OU overload da factory que aceita `ILogger?`.

**Esforço estimado:** 1h.

---

### HOUSEKEEPING-2 — Preflight de `psql` no `db/apply.sh`

**Origem:** Review F3 (N2).

**Contexto:** DX ruim quando `psql` não está no PATH — mensagem atual
é o erro bash padrão. Adicionar guard explícito no topo do script.

**Trabalho:** `command -v psql >/dev/null || { echo "psql não encontrado no PATH"; exit 127; }`.

**Esforço estimado:** 5min.

---

### HOUSEKEEPING-3 — Testar `\r\n` no renderer (Windows line endings)

**Origem:** Review F3 (N4).

**Contexto:** Teste atual cobre `\n` e `\t`. Editores Windows podem exportar com `\r\n` — a regex `\s*` cobre mas não está travado em teste.

**Esforço estimado:** 5min.

---

### CACHE-INV-3 — Paralelizar flush-all com `Task.WhenAll`

**Origem:** Review F2 (N4).

**Contexto:** `ModelPricingCache.InvalidateAsync(null)` e
`PersonaPromptTemplateCache.InvalidateAsync(null)` chamam
`PublishInvalidateAsync` num loop sequencial — N roundtrips PG. Flush
é raro mas paralelização é quase grátis.

**Esforço estimado:** 30min.

---

### PERSONA-OBS-5 — Cachear `UserIdentity` resolvido em `HttpContext.Items`

**Origem:** Review F1.

**Contexto:** `AdminAuditContext.Build` chama `UserIdentityResolver.
TryResolve(headers)` a cada GET. Em endpoints com loop/polling da UI,
são ~1-3ms redundantes. Fix: resolver uma vez no middleware e guardar
em `HttpContext.Items["identity"]`.

**Gatilho:** reclamação de latência em dashboards ou instrumentação
mostrando p99 alto em endpoints admin.

**Esforço estimado:** 2h.

---
