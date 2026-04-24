# ADR 005 — A/B testing de templates de persona via experiment scoped por project

**Status:** Aceito
**Data:** 2026-04-23
**Fase:** 6

## Context

Depois de entregar versionamento append-only (F5 / [ADR 004](004-persona-template-versioning.md)),
surgiu o caso de uso de rodar duas versions em paralelo pra medir impacto
de mudanças no prompt (tom, placeholder extra, instrução nova). Sem isso,
admins precisam decidir qual version vale a pena mantendo só "feeling",
sem métrica.

Três dimensões de decisão:

1. **Granularidade**: experiment por tenant, por project, por scope, por
   agent? Com que boundary de isolamento?
2. **Bucketing**: como dividir tráfego em dois grupos? Random por call,
   sticky por conversationId, sticky por userId?
3. **Outcome binding**: como ligar uma LLM call específica à variant que
   ela usou, pra agregar métricas?

## Decision

### 1. Scope: project-aware, 1 experiment ativo por `(ProjectId, Scope)`

Seguindo [ADR 003](003-project-as-tenancy-boundary.md), `ProjectId` é o
boundary de isolamento. Experiments herdam essa semântica: tabela
`persona_prompt_experiments` tem `ProjectId NOT NULL` + UNIQUE parcial
`(ProjectId, Scope) WHERE EndedAt IS NULL`. Garante que, dentro de um
project, no máximo um experiment roda por scope em dado momento —
evita ambiguidade de qual variant aplicar.

Scopes `global:*` e `agent:*` são cross-project por design (templates)
mas um experiment que os referencia ainda é amarrado ao `ProjectId` do
admin que o criou, pra mantermos isolamento nos dashboards.

### 2. Bucketing: determinístico por `hash(userId + experimentId) % 100`

- Sticky: mesmo usuário sempre cai na mesma variant durante o experiment
  (retries, multi-turn).
- Reproducible: mesmo userId + mesmo experimentId sempre dão o mesmo
  resultado — dashboard pode retrocalcular sem armazenar o assignment
  explicitamente por request.
- Implementação em `ExperimentAssignment.AssignVariant`:
  `SHA256(userId|experimentId)` → primeiros 4 bytes como uint → `% 100`
  → `< TrafficSplitB ? 'B' : 'A'`. Testado em
  `PersonaPromptComposerTests.AssignVariant_Split50_DistribuicaoAprox50_50`.

### 3. Outcome binding: colunas `ExperimentId`/`ExperimentVariant` em `llm_token_usage`

Composer grava o assignment no `ExecutionContext.ExperimentAssignments`
(ConcurrentDictionary por agentId). `TokenTrackingChatClient` lê de lá
ao escrever a row de telemetria. Assim cada LLM call executada sob um
experiment carrega a variant em `llm_token_usage` — e o endpoint
`GET /admin/persona-experiments/{id}/results` agrega via SQL simples:

```sql
SELECT "ExperimentVariant" AS variant,
       COUNT(*) AS samples,
       AVG("TotalTokens") AS avg_tokens,
       AVG("DurationMs") AS avg_latency
FROM llm_token_usage
WHERE "ExperimentId" = :id
GROUP BY "ExperimentVariant";
```

Decidimos **não** adicionar colunas similares em `human_interactions`:

- Maioria dos flows não tem HITL no hot path.
- Binding via `ExecutionId` (join humano com llm_token_usage) cobre o
  caso quando necessário.
- Reduz blast radius da mudança.

HITL-as-outcome fica como follow-up em PERSONA-VER ou backlog próprio.

### 4. Snapshot de Version: `VariantAVersionId` e `VariantBVersionId` apontam pra versions imutáveis

Experiment referencia 2 `VersionId` (UUID) em
`persona_prompt_template_versions`. Se o template pai é editado durante
o experiment (ou rollback muda o `ActiveVersionId`), **nada muda pras
variants** — o composer lê a version snapshot direto. Isolamento
temporal: experiment não é "contaminado" por edits concorrentes.

Trade-off: se um admin deletar uma version direto no DB (sem passar
pela API), a variant fica órfã e o composer degrada silently pra o
`ActiveVersionId` corrente. Aceito — fluxo raro e já não-suportado.

## Consequences

### Positive
- Scope de ação do experiment é claramente delimitado — uma métrica por
  variant, zero ambiguidade cross-project.
- Sticky por userId garante que usuário final não vê "oscilação" entre
  variants em conversas longas.
- Histórico de experiments encerrados fica preservado (`EndedAt` set,
  UNIQUE parcial libera o scope).
- Implementação tem ~350 LOC (repo + composer hook + controller + UI),
  reutilizando integralmente a cadeia de scope e cache de templates.

### Negative
- UNIQUE parcial bloqueia **qualquer** 2º experiment ativo no mesmo
  scope (mesmo que sejam variants completamente diferentes). Workaround:
  criar scopes distintos por feature (`project:p1:onboarding` vs
  `project:p1:cliente`) ou encerrar o ativo antes.
- Não há "paused" state — um experiment está ativo ou encerrado. Pausar
  temporariamente exigiria uma flag extra (backlog se virar necessidade).
- `llm_token_usage` ganha 2 colunas quase sempre NULL (99%+ das calls
  não participam de experiment). Índice parcial mitiga tamanho.

### Decisões não feitas (backlog)
- **Tests multi-variant (> 2 variants)**: schema assume 2-way (A/B).
  Expandir pra N exigiria refactor (`ExperimentAssignment.AssignVariant`
  + colunas variadic ou tabela separada). Não vimos caso de uso real.
- **Bucketing por segmento** (só alta suitability vê variant B): pode
  ser feito via cadeia de scope mais específica + experiment diferente
  por scope segmentado. Não precisa de feature nova.
- **Significância estatística no dashboard**: hoje mostra counts/médias
  crus. Backlog: `PERSONA-EXP-STATS` — calcular p-value via
  ExecutionAnalyticsRepository quando ≥ 200 amostras por variant.

## Files
- `db/migration_persona_experiments.sql`
- `db/migration_llm_token_usage_experiment.sql`
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/PersonaPromptExperiment.cs`
- `src/EfsAiHub.Core.Abstractions/Identity/Persona/IPersonaPromptExperimentRepository.cs`
- `src/EfsAiHub.Infra.Persistence/Postgres/PgPersonaPromptExperimentRepository.cs`
- `src/EfsAiHub.Platform.Runtime/Personalization/PersonaPromptComposer.cs` (hook + bucketing)
- `src/EfsAiHub.Platform.Runtime/Factories/TokenTrackingChatClient.cs` (outcome write)
- `src/EfsAiHub.Host.Api/Controllers/PersonaExperimentsAdminController.cs`
- `frontend/src/features/admin/PersonaExperimentsPage.tsx`
- `tests/EfsAiHub.Tests.Unit/Personas/PersonaPromptComposerTests.cs` (bucketing + variant)
- `tests/EfsAiHub.Tests.Integration/Persistence/PersonaPromptExperimentRepositoryTests.cs`
