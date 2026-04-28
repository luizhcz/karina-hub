# ADR 0015 — Evaluation Subsystem Architecture (testsets versionados, autotrigger, MS Eval API isolada)

**Status:** Aceito
**Data:** 2026-04-27

## Context

Subsistema novo: avaliação automática de agentes alinhada à Microsoft Agent Framework Evaluation API (`Microsoft.Extensions.AI.Evaluation` — `LocalEvaluator`, `FoundryEvals`, MEAI `Quality`/`Safety`). Usado para regressão automática em publish de `AgentVersion` e para A/B test manual via UI.

Quatro decisões com consequência de longo prazo precisavam ser fechadas antes de qualquer commit (PR 0 levantou cada uma como bloqueador):

1. **Vendor lock-in com Microsoft Eval API** (em preview, mudanças de API esperadas).
2. **Visibility de testsets** (alinhar com `WorkflowDefinition.Visibility=project|global` ou criar regime próprio?).
3. **Mecanismo de autotrigger em publish de `AgentVersion`** (não existe `IDomainEventDispatcher` no projeto).
4. **Semântica de `ExecutionMode.Evaluation`** (vs `Sandbox`, vs `Production`).

Decisões abaixo foram tomadas em revisão consolidada (Tech Lead, Arquiteto, IA, Performance, QA). Cada uma tem rationale + alternativa rejeitada explícita pra evitar drift quando a próxima geração de devs olhar pra cá.

## Decision

### 1. `IAgentEvaluator` é a única superfície tocando MS Eval API

Toda interação com `Microsoft.Extensions.AI.Evaluation`/`Microsoft.Agents.AI.AzureAI` fica em `EfsAiHub.Platform.Runtime/Evaluation/Adapters/`. Domínio (`EfsAiHub.Core.Agents/Evaluation/`) jamais importa tipos `Microsoft.Extensions.AI.Evaluation.*`. `MeaiResultMapper` é a fronteira: payload bruto MS → `EvaluationResult` domain entity.

**Critério de troca:** se em 12 meses o time decidir migrar para DeepEval/RAGAS/custom, swap é dos 3 adapters (`FoundryEvaluatorAdapter`, `LocalEvaluatorAdapter`, `MeaiEvaluatorAdapter`) + `MeaiResultMapper`. Domain layer e endpoints API ficam intactos.

**Anti-pattern evitado:** `AgentEvaluationResults` (tipo MS) escapando para `Core.Agents` ou para DTOs de API. Reviser arquiteto sinalizou: zero tolerância. Tests contract em `tests/EfsAiHub.Tests.Contract/Evaluation/MeaiResultMapperContractTests.cs` validam isso a cada bump de versão do SDK.

### 2. `Visibility ∈ {project, global}` desde o PR 1, alinhado com WorkflowDefinition

`evaluation_test_sets` ganha coluna `Visibility VARCHAR(32) NOT NULL DEFAULT 'project'`. Matches o padrão de `workflow_definitions.Visibility`. Default `project`; `global` exige permissão admin (validador no controller).

**Por quê alinhar agora, não em 6 meses:** plano original propôs "sempre per-project" como decisão definitiva. Reviser arquiteto apontou que negar precedente já estabelecido vira débito quando o primeiro time central pedir "safety baseline cross-tenant". `/copy?targetProject=` continua existindo (caso de uso "branch from this testset"), mas Visibility é a porta de entrada limpa.

**Blast radius de testset global:** menor que o de workflow global — testset não executa código próprio, só fornece input/expected pra avaliar agente. Risco de "tenant A vê test cases do tenant B" mitigado por `[ProjectId]` continuar required mesmo em testsets `global` (project boundary preservado para create/edit).

### 3. Autotrigger síncrono via application service (sem dispatcher de domain events)

Não existe `IDomainEventDispatcher` no projeto. Introduzir um dispatcher pra cobrir um único caso (autotrigger de eval em publish) é PR à parte com escopo cross-cutting que não cabe aqui. `IWorkflowEventBus` é canal SSE/LISTEN-NOTIFY pra streaming de execução — abusar dele para domain events polui hot path e contraria ADR 002.

**Padrão escolhido:** `AgentDefinitionApplicationService.PublishVersionAsync` orquestra `IAgentDefinitionRepository.AppendAsync` (commit do version) + `IEvaluationService.EnqueueAsync(triggerSource: AgentVersionPublished)` em try/catch. Falha em enqueue: log + métrica `evaluations.autotrigger.failed`. Publish do version commitou; eval autotrigger é best-effort.

**Idempotência em re-publish:** unique index parcial em `evaluation_runs (agent_version_id) WHERE trigger_source='AgentVersionPublished' AND status IN ('Pending','Running','Completed')` + `INSERT ... ON CONFLICT DO NOTHING`. Re-publish da mesma `AgentVersion` (retry de cliente, race) é no-op.

**Anti-pattern evitado:** `IDomainEventDispatcher` ad-hoc só pra esse caso, ou abusar de `IWorkflowEventBus` (canal SSE de execução de workflow) pra carregar domain events. Quando o projeto adotar dispatcher genuíno (PR à parte, futuro), migra-se o orchestrator.

### 4. `ExecutionMode.Evaluation` é distinto de `Sandbox` e `Production`

- **Production:** persistência completa, billing real, métricas de produção.
- **Sandbox:** LLM real, **tools mockadas** via ToolMocker, sem persistência de `chat_messages`, `LlmTokenUsage` com flag `IsSandbox=true` (custo estimado).
- **Evaluation (novo):** LLM real, **tools reais** (`ToolCalledCheck` exige tools reais executando), sem persistência de `chat_messages`, `LlmTokenUsage` persiste com `metadata.source='evaluation'` + `ExecutionId='eval:{RunId}'`. Custo real, conta no budget cap por run.

**Por quê não reusar Sandbox:** Sandbox mocka tools — eval precisa medir agente real. Misturar os modos cria condicionais frágeis em ToolMocker. Modo dedicado é mais explícito e testável.

## Consequences

### Positivas

- Vendor lock-in isolado em 4 arquivos (3 adapters + 1 mapper). Upgrade ou troca de SDK é PR pequeno.
- Visibility alinhado com workflows = modelo mental único pro usuário operador.
- Autotrigger sem dispatcher novo: zero infra cross-cutting nova; `AgentDefinitionApplicationService` é convenção já familiar (espelha `WorkflowDefinitionApplicationService`).
- `ExecutionMode.Evaluation` semântica explícita facilita futuros modos (`ExecutionMode.Replay`?) sem refactor.
- Idempotência em autotrigger via DB constraint (não em memória) sobrevive a crash do worker.

### Negativas (aceitas)

- Application service síncrono significa que enqueue de autotrigger compete com latência da API de publish (~30-100ms a mais). Aceitável: publish é operação rara, não hot path.
- Visibility `global` em testsets exige UI/policy de governança (admin-only edit) que fica em `EvaluationTestSetsController`. Custo de implementação trivial, mas existe.
- Sem dispatcher de domain events significa que se o time adicionar mais 3 use-cases similares (publish dispara X, Y, Z), `AgentDefinitionApplicationService` cresce e vira candidato a refactor pra dispatcher real. Aceitável: cross essa ponte quando ela aparecer.

## Invariantes

1. **`AgentEvaluationResults` MS jamais escapa de `Platform.Runtime.Evaluation`.** `Core.Agents.Evaluation` só conhece `EvaluationResult` (domain).
2. **`evaluation_runs` com `trigger_source='AgentVersionPublished'` é único por `(agent_version_id, status not Cancelled/Failed)`** — re-publish é no-op.
3. **`AgentDefinitionApplicationService.PublishVersionAsync` é a única porta de entrada para criar `AgentVersion` com autotrigger.** Endpoints/controllers não chamam `IAgentVersionRepository.AppendAsync` diretamente.
4. **`ExecutionMode.Evaluation` força `metadata.source='evaluation'` em `LlmTokenUsage`** + `ExecutionId='eval:{RunId}'`.
5. **`EvaluationRun.EvaluatorConfigVersionId` aponta para `evaluator_config_versions` (snapshot imutável)**, nunca para o header `evaluator_configs`. Revisão arquiteto: snapshot é a unidade reproducível.
6. **`evaluation_test_set_versions (test_set_id, content_hash)` é único por `Status != 'Deprecated'`** — race em publish concorrente é no-op.
7. **`Visibility='global'` em testsets exige permissão admin** (não bypass em raw SQL).

## Anti-patterns evitados

- **Domain dispatcher ad-hoc.** `AgentDefinitionApplicationService` substitui canonicamente; futuro dispatcher real é PR explícito, não acidental.
- **Tipos MS (`AgentEvaluationResults`, `EvaluationContext`, `EvaluatorScore`) em DTOs ou domain.** Mapper na fronteira.
- **`IWorkflowEventBus` como pub/sub de domain events.** É canal SSE de execução de workflow — overload conceitual proibido.
- **`evaluations.case.score{agent_id=UUID}`** — alta cardinalidade quebra Prometheus em multi-tenant. `agent_definition_name` é a tag (low-card, ~50 nomes únicos por tenant).
- **`UPDATE evaluation_runs SET cases_completed = cases_completed + 1`** em hot path — contadores rolling vão para `evaluation_run_progress` (tabela auxiliar) com `INSERT ... ON CONFLICT DO UPDATE`.

## Riscos remanescentes (priorizados)

1. **Alto — MS Eval API em preview muda contrato.** Mitigação: contract test em `tests/Fixtures/meai_payload_v*.json` re-rodado a cada bump de SDK. Pin exato de versão; upgrade só com PR explícito.
2. **Médio — Crash do worker entre commit e enqueue de autotrigger.** Publish commitou, autotrigger nunca rodou. Métrica `evaluations.autotrigger.failed` + alerta Grafana detectam. Catch-up no startup ("varrer AgentVersions Published nas últimas 5min sem run associada") fica no backlog operacional, não no código de runtime.
3. **Médio — Drift entre testset Published e tools removidas em `AgentVersion`.** Validador `EnqueueAsync` rejeita 400 se `ExpectedToolCalls[].name` não está em tools do agente. Sem isso, autotrigger vira fonte permanente de falso positivo após refactor de tools.
4. **Baixo — Self-enhancement bias quando judge==agent_model.** Banner UI alerta; `judge_model` registrado em `evaluation_results.judge_model`. Decisão: warning, não bloqueio (custos altos de exigir judge de família distinta).

## Caminho de evolução

- **PR 7 (futuro):** dispatcher de domain events real (`IDomainEventDispatcher` baseado em outbox table). Migrar `AgentDefinitionApplicationService` pra publicar `AgentVersionPublished` via dispatcher; handler novo absorve a chamada de `EvaluationService`. Trigger: 3+ use-cases similares.
- **PR 8 (futuro):** smoke test pós-deploy automatizado validando "publish AgentVersion → run aparece em ≤5s". Hoje verificação manual.
- **Cache de eval results ativo por default** depende de medirmos varianza real entre runs idênticas. Coleta em produção primeiro.

## Critical files

- `src/EfsAiHub.Core.Agents/Evaluation/EvaluationRun.cs` — entidade
- `src/EfsAiHub.Core.Agents/Evaluation/EvaluatorConfigVersion.cs` — snapshot reproducível
- `src/EfsAiHub.Core.Agents/Evaluation/EvaluationTestSet.cs` — header com Visibility
- `src/EfsAiHub.Platform.Runtime/Evaluation/IAgentEvaluator.cs` — única superfície MS Eval API (PR 2)
- `src/EfsAiHub.Platform.Runtime/Evaluation/MeaiResultMapper.cs` — fronteira MS → domain (PR 2)
- `src/EfsAiHub.Host.Api/Chat/Services/AgentDefinitionApplicationService.cs` — orquestrador autotrigger síncrono (PR 2)
- `src/EfsAiHub.Core.Abstractions/Execution/ExecutionMode.cs` — adicionar `Evaluation`
- `db/schemas.sql:813` — seções 21-27 + ALTER `agent_definitions` + ALTER `llm_token_usage`
