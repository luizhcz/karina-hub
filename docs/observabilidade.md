# Observabilidade — EfsAiHub

Catálogo de spans, métricas e audit trails expostos pela plataforma.
Fonte de verdade pros dashboards Grafana + SIEM.

---

## Spans (OpenTelemetry)

Definidos em `src/EfsAiHub.Infra.Observability/Tracing/ActivitySources.cs`.

| ActivitySource                    | Nome de span                  | Onde aparece |
|-----------------------------------|-------------------------------|--------------|
| `EfsAiHub.Api.Execution`          | `WorkflowRun`                 | `WorkflowRunnerService` |
| `EfsAiHub.Api.AgentInvocation`    | `AgentInvocation`             | `NodeStateTracker` |
| `EfsAiHub.Api.LlmCall`            | `LLMCall`, `LLMCall.Streaming`| `TokenTrackingChatClient` |
| `EfsAiHub.Api.ToolCall`           | `ToolCall`                    | `TrackedAIFunction` |
| `EfsAiHub.Api.EventBus`           | `EventBus.Publish/Subscribe`  | `PgEventBus` |

### Tags padrão (LlmCall)

| Tag                   | Tipo   | Significado |
|-----------------------|--------|-------------|
| `agent.id`            | string | ID do agente |
| `model.id`            | string | DeploymentName do modelo |
| `tokens.input`        | int    | Input tokens totais |
| `tokens.output`       | int    | Output tokens totais |
| `tokens.total`        | int    | Soma (ou valor da API) |
| `tokens.cached`       | int    | **Novo (F1)** — input tokens servidos do prompt cache da OpenAI. 0 = sem cache hit. Subset de `tokens.input`. |
| `duration.ms`         | double | Duração da chamada LLM em ms |
| `cost.usd.delta`      | double | Custo incremental desta chamada (fase 2 de billing) |
| `cost.usd.total`      | double | Custo acumulado da execução |

### Tags de persona (novo em F1)

Populadas via `ActivityExtensions.SetPersonaTags(UserPersona?)` em
`src/EfsAiHub.Infra.Observability/Tracing/ActivityExtensions.cs`. No-op
quando persona é null ou Anonymous.

| Tag                       | Aplicável a | Conteúdo |
|---------------------------|-------------|----------|
| `persona.user_type`       | cliente/admin | `"cliente"` ou `"admin"` |
| `persona.user_id_hash`    | cliente/admin | SHA-256 truncado do UserId (16 chars hex). **LGPD**: ID raw nunca vai em span. |
| `persona.segment`         | cliente | `BusinessSegment` (ex: `private`, `varejo`) |
| `persona.suitability`     | cliente | `SuitabilityLevel` |
| `persona.partner_type`    | admin   | `PartnerType` (DEFAULT/CONSULTOR/GESTOR/ADVISORS) |
| `persona.wm`              | admin   | `true` quando `IsWm` (só aparece se true) |

---

## Métricas (Meter `EfsAiHub.Platform`)

Registradas em `src/EfsAiHub.Infra.Observability/Metrics/MetricsRegistry.cs`.

### Agent / LLM

| Métrica                          | Tipo       | Tags                           | Significado |
|----------------------------------|------------|--------------------------------|-------------|
| `agent.invocation.duration`      | Histogram  | `agent.id`, `workflow.id`      | Duração total de invocação de agente (s) |
| `agent.tokens.used`              | Histogram  | `agent_id`, `model_id`         | Tokens totais por chamada |
| `agent.cost.usd`                 | Histogram  | `agent_id`, `model_id`         | Custo USD por chamada |
| `llm.retries`                    | Counter    | `agent_id`, `model_id`, `outcome` | Tentativas de retry |
| `budget.exceeded.kills`          | Counter    | —                              | Abortos por budget estourado |

### Persona

| Métrica                           | Tipo       | Tags                  | Significado |
|-----------------------------------|------------|-----------------------|-------------|
| `persona.resolution.duration_ms`  | Histogram  | `outcome`             | Latência de resolução (`cache_hit_l1 \| cache_hit_l2 \| api_hit \| fallback`) |
| `persona.resolution.failures`     | Counter    | —                     | Fallbacks Anonymous por falha da API externa |
| `persona.prompt.compose.chars`    | Histogram  | `user_type`           | **Novo (F1)** — tamanho em chars do `SystemSection` renderizado pelo composer. Detecta inchaço de template. Emitida em `PersonaPromptComposer.ComposeAsync`. |

### EventBus

| Métrica                                  | Tipo       | Tags      | Significado |
|------------------------------------------|------------|-----------|-------------|
| `eventbus.subscribe.setup_errors`        | Counter    | `phase`   | Falhas no setup LISTEN do subscriber |

---

## Audit Log (admin_audit_log)

Gravado por `IAdminAuditLogger` (fire-and-log — falhas não quebram request).

### Actions

| Action   | Descrição |
|----------|-----------|
| `create` | Upsert que criou novo registro |
| `update` | Upsert que modificou registro existente |
| `delete` | DELETE |
| `read`   | **Novo (F1)** — consulta a recurso sensível. Uso seletivo (não em todo GET) — começou pela feature persona por exigência LGPD art. 37. |

### Resources

- `project`, `agent`, `workflow`, `skill`
- `model_pricing`, `document_intelligence_pricing`
- `mcp_server`
- `persona_cache` — consulta / invalidação da persona resolvida
- `persona_prompt_template` — CRUD + read de templates

### Read audit instrumentado (F1)

| Endpoint                                      | Action | ResourceId |
|-----------------------------------------------|--------|------------|
| `GET /api/admin/personas/{userId}?userType=X` | read   | `{userType}:{userId}` |
| `GET /api/admin/persona-templates`            | read   | `*`        |
| `GET /api/admin/persona-templates/{id}`       | read   | `{id}`     |

Outros controllers admin (pricing, agents, workflows) **não** estão
instrumentados com read audit — decisão custo/benefício. Estender caso
auditoria externa peça.

---

## Cached tokens — ADR e fluxo

Ver [ADR 000](adr/000-opensdk-shape.md). Fluxo:

```
OpenAI HTTP response
  usage.prompt_tokens_details.cached_tokens
        ↓
OpenAI SDK 2.10.0
  ChatTokenUsage.InputTokenDetails.CachedTokenCount
        ↓  (Microsoft.Extensions.AI.OpenAI 10.4.0 adapter)
Microsoft.Extensions.AI.UsageDetails
  CachedInputTokenCount (long?)
        ↓  (TokenTrackingChatClient.TrackUsage)
LlmTokenUsage.CachedTokens → aihub.llm_token_usage.CachedTokens
        +
Activity.SetTag("tokens.cached", N)
```

**prompt_cache_key NÃO é setado** (ver ADR 000): SDK ainda não suporta.
Mitigação via prefixo invariante no system message (cache hit provável).

---

## Queries úteis

### Cache hit rate

```sql
SELECT
  DATE_TRUNC('hour', "CreatedAt") AS hour,
  SUM("CachedTokens") AS cached,
  SUM("InputTokens") AS total_input,
  ROUND(100.0 * SUM("CachedTokens") / NULLIF(SUM("InputTokens"), 0), 1) AS hit_pct
FROM aihub.llm_token_usage
WHERE "CreatedAt" > now() - INTERVAL '7 days'
GROUP BY 1 ORDER BY 1;
```

### Reads de persona por actor

```sql
SELECT "ActorUserId", COUNT(*) AS reads_last_24h
FROM aihub.admin_audit_log
WHERE "Action" = 'read'
  AND "ResourceType" IN ('persona_cache','persona_prompt_template')
  AND "Timestamp" > now() - INTERVAL '24 hours'
GROUP BY 1 ORDER BY 2 DESC;
```
