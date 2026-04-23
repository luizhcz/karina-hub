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
