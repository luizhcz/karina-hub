# ADR 0019 — Self-Contained Agent Snapshot + Runtime Puro Leitor

**Status:** Aceito
**Data:** 2026-05-21
**Contexto:** Continuação do épico "Workflows multi-projeto — Pinning Federated"

## Contexto

[ADR 0018](0018-lossless-agent-version-pinning.md) lossless-pinou os campos diretos do agente (`Description`, `Metadata`, `FallbackProvider`, `Tools` cheias) em `AgentVersion`. Mas o runtime ainda fazia 6 lookups dinâmicos por turn em `AgentFactory.CreateAgentsForWorkflowAsync`:

1. `IAgentPromptRepository.GetActivePromptAsync` — substitui `Instructions` pelo master prompt.
2. `IGenericToolBinder.BindAsync` — resolve cada `Tools[*]` com `Type=generic_http` via `GenericToolRepository`.
3. `IPredefinedModelBinder.BindAsync` — hidrata `DeploymentName`/`Endpoint`/`Provider.Type` a partir do preset.
4. `ISkillResolver.ResolveAsync` — mescla tools/instructions addendum das skills referenciadas.
5. `IAgentRouterIntentLinkRepository.ListIntentsForAgentAsync` — carrega o set de intents do Router.
6. `_agentRepo.GetByIdAsync` (governance source live row) + `InjectProjectCredentials` — esses dois ficam (mutáveis por design, legítimos).

Consequências:
- **Drift**: edit de intent/tool/model em UI alterava o comportamento de **agentes pinados em versões antigas** sem nova revision — pin não era source of truth, só governança era.
- **Latência**: 6 round-trips DB por turn (cacheados, mas adicionavam pressão).
- **Versionamento não-lossless de verdade**: `AgentVersion` v2 ainda dependia de tabelas externas pra hidratar.

## Decisão

O **boundary de escrita** (`POST/PUT /agents/{id}`, `ApproveAsync` do draft, propagation) materializa o estado final autocontido. O **boundary de leitura** (`GET /agents/{id}`) decompõe pra forma autoral. Runtime nunca volta às dependências.

### Estado persistido em `agent_definitions.Data` (jsonb)

Sempre o "final renderizado":

```json
{
  "Instructions": "Você é um Trader...\n\n<!-- aihub:auto-intents -->\n# Intenções\n- consultar_cotacao: ...\n<!-- aihub:auto-intents-end -->",
  "Tools": [{
    "Type": "generic_http",
    "GenericToolId": "tool-get-quote",
    "UrlTemplate": "https://...",
    "InputSchemaJson": "...",
    "OutputSchemaJson": "...",
    "OutputProjectionMode": "Project",
    "TimeoutSecondsOverride": 30,
    "IsExclusive": false
  }],
  "Model": {
    "PredefinedModelId": "gpt-4o-default",
    "DeploymentName": "gpt-4o",
    "Endpoint": "https://...",
    "DefaultTemperature": 0.7
  },
  "StructuredOutput": {
    "Schema": "{ ... enum: [\"consultar_cotacao\", \"out_of_scope\"] inline ... }"
  },
  "RouterIntentIds": ["intent-cotacao", "intent-oos"]
}
```

### Boundary HTTP

**POST/PUT** recebe autoral + IDs; backend roda `IAgentDefinitionComposer.ComposeAsync` antes do `UpsertAsync`.

**GET** roda `IAgentDefinitionDecomposer.Decompose` antes do `AgentResponse.FromDomain`; cliente recebe a mesma forma que enviou.

Identidade garantida por construção:

```
Decompose(Compose(x)) ≡ x
```

via marcadores HTML estáveis dentro de `Instructions`:

```
<!-- aihub:auto-intents -->...<!-- aihub:auto-intents-end -->
<!-- aihub:auto-skills -->...<!-- aihub:auto-skills-end -->
<!-- aihub:auto-worker-scope -->...<!-- aihub:auto-worker-scope-end -->
```

Tools mergeadas a partir de skill carregam `SourceSkillId`; decomposer remove. Generic tools voltam pra forma compacta (`{Type, Name, GenericToolId}`). Model com `PredefinedModelId` perde `Endpoint`/`Provider` expandidos.

### Runtime puro leitor

`AgentFactory.CreateAgentAsync` reduz a 4 passos:

1. `InjectProjectCredentials` (legítimo — credenciais runtime).
2. `TrackAgentVersionAsync` (audit).
3. `ResolveProvider` (DI).
4. `ChatOptionsBuilder.BuildAgentOptions(definition)` — lê Instructions/Tools/Model/StructuredOutput direto do snapshot, zero mutação.

`ChatOptionsBuilder.BuildFunctionTools` para `Type=generic_http` reconstrói `DynamicGenericAIFunction` direto dos campos inline (UrlTemplate, schemas, params, headers) usando `IGenericToolExecutor` injetado — sem `IFunctionToolRegistry.TryGet`.

### Propagação automática

Edit em dep dispara `IAgentDependencyPropagator`:

- `PropagateRouterIntentEditAsync(intentId)`
- `PropagateGenericToolEditAsync(toolId)`
- `PropagatePredefinedModelEditAsync(modelId)`
- `PropagateAgentPromptChangeAsync(agentId)`

Pra cada agente afetado: `GetByIdAsync` → `Decompose` → `ComposeAsync` (re-resolve deps) → `UpsertAsync` (dual-write via ContentHash dedup).

Trail em `agent_approval_history` com `Action=AutoApproved`, novo `Tier=PropagatedDependency`, `ActorUserId=system:dependency-propagator`.

### Delete blocking

Análogo ao já existente em `RouterIntent`:
- `GenericToolService.DeleteAsync` → `GenericToolInUseException(toolId, agentIds)` se referenciado.
- `PredefinedModelService.DeleteAsync` → `PredefinedModelInUseException(modelId, agentIds)`.

Queries via JSONB (`jsonb_path_exists` / `#>>`) no `PgAgentDefinitionRepository`.

### Backfill obrigatório

`AgentVersionBackfillService` no startup percorre todos `agent_definitions`: decompose → compose → upsert. Idempotente por ContentHash. Falha em qualquer agente propaga via `StartAsync` e bloqueia inicialização — refs quebradas surgem no boot, não no primeiro turn.

## Consequências

### Positivas

- **Runtime determinístico**: snapshot pinado é byte-a-byte o que executou na publish. Sem drift.
- **Latência**: 6 lookups dinâmicos → 0. Runtime executa `agent_versions` (snapshot) + `agent_definitions` (governance) + `projects` (credenciais) = 3 queries por turn.
- **Net reduction**: ~5 binders/resolvers deletados (IGenericToolBinder, IPredefinedModelBinder, ResolveActivePrompt, ResolveSkills, ResolveRouterIntentsAsync, AppendRouterIntentBlockIfApplicable, AppendWorkerScopeBlockIfApplicable, BuildJsonSchemaFormat, BuildResponseFormatWithMemory). Composer/decomposer/propagator/markers compensam com superfície menor.
- **Falha cedo**: composer lança `DomainException` no save se dep referenciada não existe; backfill bloqueia boot se algum agente legado tem ref quebrada.

### Negativas

- **Backfill no startup**: agentes na ordem de milhares aumentam tempo de boot. Mitigação: idempotente (próximo boot é no-op pra agents já compostos). Batch/cursor fica como follow-up se tornar dor.
- **Decompose com regex de markers**: depende dos marcadores serem inalterados. Mitigação: marcadores são HTML comments com prefixo único (`aihub:auto-`); composer escapa marcadores literais no autoral antes de injetar.
- **Front depende de GET devolver autoral**: cliente continua editando como antes; `Instructions` no GET passa a ser **apenas autoral** (sem blocos auto-gerados). Mudança observável documentada em release notes.
- **Cross-tenant propagation**: `PredefinedModel` é global e pode afetar agentes de tenants distintos. V1 propaga no escopo do caller; cross-tenant não toca cache do outro tenant (cache miss benigno, não stale).

## Implementação

Commits no branch `feat/lossless-snapshot-composer` (em ordem):

1. `ec752f1` feat(agent): expand domain with self-contained tool/model/skill fields
2. `e9d3abe` feat(agent): introduce composer/decomposer with stable instruction markers
3. `21f7629` feat(agent): route writes through composer and reads through decomposer
4. `2724ab8` refactor(agent-factory): runtime reads only from AgentVersion snapshot
5. `4ccb305` feat(agent): propagate dependency edits, block delete while referenced, mandatory backfill
6. `chore(agent): drop obsolete runtime resolvers and document architecture (ADR 0019)` (este)

### Migrations

- `db/migrations/008_agent_approval_history_propagated_dependency_tier.sql` — estende CHECK do tier pra aceitar `'PropagatedDependency'`.

### Arquivos-chave

- `src/EfsAiHub.Core.Agents/Services/AgentDefinitionComposer.cs` — write boundary.
- `src/EfsAiHub.Core.Agents/Services/AgentDefinitionDecomposer.cs` — read boundary.
- `src/EfsAiHub.Core.Agents/Services/PromptRenderer.cs` — render determinístico de blocos intents/skills/worker scope.
- `src/EfsAiHub.Core.Agents/Services/OutputSchemaRenderer.cs` — injeta enum de intents inline no schema do Router.
- `src/EfsAiHub.Core.Agents/Services/AgentInstructionsMarkers.cs` — constantes dos marcadores.
- `src/EfsAiHub.Core.Agents/Services/AgentDependencyPropagator.cs` — propagator.
- `src/EfsAiHub.Platform.Runtime/Factories/AgentFactory.cs` — runtime simplificado.
- `src/EfsAiHub.Platform.Runtime/Factories/ChatOptionsBuilder.cs` — lê snapshot direto.
- `src/EfsAiHub.Host.Worker/Services/AgentVersionBackfillService.cs` — backfill obrigatório.
