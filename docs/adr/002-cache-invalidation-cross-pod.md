# ADR 002 — Invalidação de cache cross-pod via Postgres LISTEN/NOTIFY

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 2

## Context

Antes da F2, os caches L1 in-memory (`CachedPersonaProvider`,
`PersonaPromptTemplateCache`, `ModelPricingCache`,
`DocumentIntelligencePricingCache`) eram **independentes por pod**:
quando o admin alterava um template via UI, apenas o L1 do pod que
recebeu o request era invalidado. Os outros pods serviam dados stale
até o TTL expirar (30-60s para persona, 5min para pricing).

Em deploy multi-réplica isso é inaceitável — mudança de template
levaria minutos pra se propagar, e load balancer pode empurrar
requests ida-e-volta entre pod novo (atualizado) e pod velho (stale).

## Decision

Reusar a infra de pub/sub que já existe no repo: **Postgres
LISTEN/NOTIFY via `PgNotifyDispatcher`**. Não criar dependência
nova (Redis pub/sub) e não duplicar a infra (uma segunda conexão
LISTEN dedicada).

### Arquitetura

```
┌─ admin POST /admin/persona-templates (pod A)
│
├─ _cache.InvalidateAsync(scope)
│    ├─ removeu L1 do pod A
│    ├─ removeu L2 (Redis)
│    └─ _invalidationBus.PublishInvalidateAsync(cacheName, scope)
│            │
│            └─ SELECT pg_notify('efs_cache_invalidate', JSON)
│                   │
│                   └─ todos os pods escutando recebem
│
├─ pod B: PgNotifyDispatcher.OnNotification(channel='efs_cache_invalidate')
│    ├─ parse payload
│    ├─ demux por cacheName
│    └─ invoca handler registrado pelo Subscribe
│           │
│           └─ PgCacheInvalidationBus wrapper:
│                 se sourcePodId == próprio pod, IGNORA (echo filter)
│                 senão, chama handler: _local.TryRemove(key)
```

### Pontos-chave

1. **Canal novo `efs_cache_invalidate`** adicionado ao LISTEN que o
   `PgNotifyDispatcher` já mantém. Sem nova conexão; Postgres aceita
   múltiplos LISTEN na mesma sessão.
2. **`ICacheInvalidationBus`** (abstração em `Core.Abstractions/Events/`)
   é a única API que os caches usam. Implementação
   `PgCacheInvalidationBus` vive em `Infra.Messaging`.
3. **Filtro de echo** via `SourcePodId` (default: `Environment.MachineName`).
   Evita loop infinito e refetch inútil quando o próprio pod publica.
4. **Best-effort**: falha de publish é logada, TTL (60s L1) é safety net.
   Falha de subscribe handler é isolada em try/catch no Dispatcher.

### Cache names registrados

| CacheName         | Classe                              | Prefix Redis  |
|-------------------|-------------------------------------|---------------|
| `persona`         | `CachedPersonaProvider`             | `persona:`    |
| `persona-tpl`     | `PersonaPromptTemplateCache`        | `persona-tpl:`|
| `model-pricing`   | `ModelPricingCache`                 | `pricing:`    |
| `di-pricing`      | `DocumentIntelligencePricingCache`  | `di-pricing:` |

## Alternatives considered

- **Redis pub/sub**: menor latência de propagação (~1ms vs ~10ms do
  NOTIFY), mas adiciona complexidade (conexão dedicada, handling de
  reconexão paralela ao SSE) sem ganho prático — 10ms é negligenciável
  frente aos TTLs de 60s/5min. **Rejeitado** pra manter uniformidade
  com o resto da stack.
- **`PgCrossNodeBus` estendido**: publisher + subscriber no mesmo
  tipo. Poderia ser, mas o papel dele hoje é específico (cancel exec,
  HITL resolved). Separar bus por finalidade deixa as semânticas
  distintas. **Rejeitado** por clareza.
- **Bus genérico em cima do dispatcher** (channel-agnostic): refactor
  maior, atinge `PgEventBus` que é canal hot-path. **Rejeitado** —
  YAGNI. Generalização incremental só no dispatcher é mais segura.

## Consequences

**Positivo:**
- Mudanças administrativas (template upsert, pricing update, persona
  invalidate via LGPD) propagam em <1s cross-pod.
- Zero dependência nova; reusa conn PG já aberta.
- Fácil observar (pg_stat_activity mostra LISTEN, logs debug mostram
  entrega).

**Negativo:**
- NOTIFY sem garantia de entrega — pods em janela de reconexão (≤10s
  worst case com backoff) perdem eventos. TTL do L1 absorve.
- Payload limitado a 8KB (limite Postgres NOTIFY). Cache keys são
  curtos, não chega perto.

**Follow-ups:**
- Testar cenário multi-pod via `docker compose up --scale backend=N`
  em ambiente que suporte múltiplos binds (k8s, CI).
- Considerar métrica `cache.invalidation.{published,received}` no
  backlog se surgir necessidade de troubleshooting.

## References

- `src/EfsAiHub.Core.Abstractions/Events/ICacheInvalidationBus.cs`
- `src/EfsAiHub.Infra.Messaging/PgNotifyDispatcher.cs` (canal novo + subscribe/unsubscribe)
- `src/EfsAiHub.Infra.Messaging/PgCacheInvalidationBus.cs`
- `src/EfsAiHub.Platform.Runtime/Execution/{CachedPersonaProvider,PersonaPromptTemplateCache,ModelPricingCache,DocumentIntelligencePricingCache}.cs`
