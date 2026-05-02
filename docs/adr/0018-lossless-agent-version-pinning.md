# ADR 0018 — Lossless AgentVersion Pinning + Breaking/Patch Policy

**Status:** Aceito
**Data:** 2026-05-01
**Contexto:** Épico "Workflows multi-projeto — Pinning Federated" (Fase 1)

## Contexto

ADR 0017 introduziu `WorkflowAgentReference.AgentVersionId?` (pin opcional) mas a materialização lossless de `AgentVersion → AgentDefinition` ficou pendente. O `AgentVersion` record guardava snapshots por campo (`AgentModelSnapshot`, `AgentProviderSnapshot`, `ToolFingerprints` apenas com hashes, `AgentMiddlewareSnapshot`), faltando:

- `Description`, `Metadata`, `FallbackProvider`
- Tools cheias (Type, Name, JsonSchema/FingerprintHash, McpServerId, ServerLabel/Url, AllowedTools, Headers, ConnectionId, RequireApproval, RequiresApproval, ConnectionId)

Sem esses campos, `AgentFactory.CreateAgentsForWorkflowAsync` quando recebia pin não conseguia reconstruir `AgentDefinition` determinístico — caía no live row corrente e logava warning. Pin era basicamente decorativo.

Além disso, faltava semântica de **breaking change**: caller pinava em v1; owner publicava v2 corrigindo um bug crítico (sem mudança de contrato); workflow continuava em v1 com bug. Sem mecanismo de propagação automática de patches, pin obrigatório (Phase 2) seria operacionalmente custoso — toda correção de owner exigiria intervenção manual de cada caller.

## Decisão

`AgentVersion` torna-se **source of truth lossless** da execução, com schema versionado e política híbrida breaking/patch.

### Domain — campos novos no record

```csharp
public sealed record AgentVersion(
    // Existing positional fields...
    string ContentHash,
    // Lossless additions (SchemaVersion >= 2). Default null/false/1 pra BC com snapshots v1.
    string? Description = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    AgentProviderSnapshot? FallbackProvider = null,
    IReadOnlyList<AgentToolSnapshot>? Tools = null,
    bool? BreakingChange = null,
    int SchemaVersion = 1)
```

`AgentToolSnapshot` é record novo com TODOS os campos de `AgentToolDefinition` (não só fingerprint).

### Schema versioning v1 → v2

- **v1 (legacy)**: snapshots por campo, lossy. `Tools=null`, sem Description/Metadata/FallbackProvider. AgentFactory cai no path legado (live definition + warning).
- **v2 (lossless)**: `Tools` cheias persistidas; `ToDefinition()` reconstrói `AgentDefinition` determinístico.

`SchemaVersion=1` é default pra rows pré-feature; novos snapshots emitidos por `FromDefinition` setam `SchemaVersion=2`. Persistido como coluna promoted (não só dentro do JSON snapshot) pra permitir queries SQL diretas.

### Hydrate de governança via row, NÃO snapshot

**Decisão chave**: `Visibility`, `ProjectId`, `TenantId`, `AllowedProjectIds` são hidratados da `agent_definitions` row corrente via `governanceSource` parameter de `ToDefinition()`, **não do snapshot**.

**Razão**: governança é **mutável e cross-cutting**. Owner que demote `global → project` deve afetar workflows pinados imediatamente. Se governança ficasse congelada no snapshot, agent demoted continuaria exposto via versions antigas pinadas — vazamento silencioso.

Snapshot carrega *behavior* (prompt, model, tools, middlewares, schema, fallback). Row carrega *governança* (acesso, ownership, tenant boundary). Decoupling intencional.

```csharp
public AgentDefinition ToDefinition(AgentDefinition? governanceSource = null)
{
    // ... behavior fields vêm do snapshot ...
    return new AgentDefinition
    {
        // Behavior:
        Description = Description, Tools = ..., Middlewares = ..., FallbackProvider = ...,
        // Governance vem do estado vivo (mutável, cross-cutting):
        ProjectId = governanceSource?.ProjectId ?? "default",
        Visibility = governanceSource?.Visibility ?? "project",
        TenantId = governanceSource?.TenantId ?? "default",
        AllowedProjectIds = governanceSource?.AllowedProjectIds,
    };
}
```

### Política breaking/patch — `BreakingChange: bool?`

Owner declara intent no momento do publish:

- **`BreakingChange=true`**: workflows pinados em ancestor desta version NÃO recebem patch propagation; ficam presos no snapshot pinado. Exige `ChangeReason` não-vazio (rastreabilidade pra caller decidir migrar). Validado em `AgentVersion.EnsureInvariants()`.
- **`BreakingChange=false`**: patch — propaga pra workflows pinados em ancestors sem breaking entre eles.
- **`BreakingChange=null`** (legacy/auto-snapshot): tratado conservativamente como breaking (não propaga). Versions pré-feature ficam null automaticamente.

`IAgentVersionRepository.GetAncestorBreakingAsync(agentDefId, fromRevExclusive, toRevInclusive)` retorna a primeira version com `BreakingChange=true` no range half-open. Index parcial cobre o predicate hot path: `IX_agent_versions_AgentDefId_Breaking ON agent_versions(AgentDefinitionId, Revision) WHERE BreakingChange = TRUE`.

### `ResolveEffectiveAsync` — algoritmo

```
ResolveEffectiveAsync(agentDefId, pinnedVersionId):
  pinned = GetById(pinnedVersionId)  // throw se inexistente ou de outro agent
  current = GetCurrent(agentDefId)
  if current is null         → return pinned (caller pinou em estado pré-publish)
  if pinned.Revision >= current.Revision → return pinned
  breaking = GetAncestorBreaking(pinned.Revision, current.Revision)
  if breaking is null        → return current  (patch propaga)
  else                       → return pinned   (breaking bloqueia)
```

### Matriz de comportamento

| Pin set | SchemaVersion | BreakingChange entre pin e current | Resultado | Métrica `strategy` |
|---|---|---|---|---|
| Não | — | — | live definition (current) | (sem métrica) |
| Sim | v2 | Pin == current | pin (= current) | `exact` |
| Sim | v2 | Não há breaking entre eles | current (patch propaga) | `propagated` |
| Sim | v2 | Há breaking entre eles | snapshot pinado | `exact` |
| Sim | v2 | Pin > current (raro) | pin | `exact` |
| Sim | v1 (legacy lossy) | — | live definition + warning | `legacy_fallback` |
| Sim | — | `_versionRepo` não-injetado (testes) | live definition | `no_version_repo` |
| Sim | — | governance row sumiu (orphan) | throw + métrica | (`governance_missing`) |

### Save-time validation

`WorkflowValidator.ValidateAgentReferencesAsync` valida pin no save: `GetByIdAsync` confirma existência + `pinned.AgentDefinitionId` confirma ownership. Erros surfacam antes de runtime, com mensagem clara.

### Publish flow

`AgentService.PublishVersionAsync(agentId, breakingChange, changeReason?, createdBy?)`:

- Owner gate: `UnauthorizedAccessException` quando project caller difere do owner.
- Captura prompt ativo via `IAgentPromptRepository.GetActivePromptWithVersionAsync` (best-effort).
- Chama `AgentVersion.FromDefinition(definition, revision, prompt, ..., breakingChange)`.
- `IAgentVersionRepository.AppendAsync` chama `EnsureInvariants()` antes de tocar DB (rejeita `BreakingChange=true` sem `ChangeReason`).
- Idempotência por ContentHash: re-publish sem mudança retorna existing AgentVersion.
- Audit `agent.version_published` emitido apenas em publish efetivo (não em no-op idempotente).

### Auto-snapshot via `UpsertAsync` continua

`PgAgentDefinitionRepository.UpsertAsync` ainda cria snapshot automático em todo PUT `/api/agents/{id}` — agora com `SchemaVersion=2`, mas `BreakingChange=null` (intent não declarado). Caller que quer declarar breaking usa `PublishVersionAsync` explicitamente. Endpoint dedicado `POST /api/agents/{id}/versions` vem em Phase 3.A do épico.

### Observabilidade

- `agents.version_pin_resolutions_total{strategy=exact|propagated|legacy_fallback|no_version_repo, agent_id}` — visibilidade de quanto tempo pins ficam em ancestor exato vs propagam.
- `agents.version_lossless_governance_missing_total{agent_id}` — orphan pins (alimenta health check).
- `agents.version_lossless_roundtrip_failures_total{agent_version_id}` — sev1, JSON snapshot corrompido força fallback defensivo.
- Health check `WorkflowAgentVersionHealthCheck` (tag `[ready, sharing, pinning]`) reporta `Degraded` quando há orphan AgentVersions.
- Audit constants: `AgentVersionPublished`, `AgentVersionLosslessRoundtripFailed` (esta última declarada pra emissão futura via dispatcher).

## Alternativas rejeitadas

1. **Re-snapshot histórico** (backfill v1 → v2 retroativo): caro (escrita massiva) e potencialmente impreciso (live definition pode ter divergido do estado original do snapshot). Versions antigas continuam lossy; UI/runtime detecta e cai pro path legado. Re-publish manual via DBA script no runbook quando lossless é exigido.
2. **Eager bulk auto-pin** de workflows legados ao habilitar `MandatoryPin=true`: causa lock + audit explosion + race entre 2 instâncias. Phase 2 prefere lazy auto-pin no first AgentFactory call.
3. **Governança dentro do snapshot**: rejeitada por motivo central acima — bloquearia mudanças cross-cutting de owner em workflows pinados antigos.
4. **`BreakingChange` no `CreateAgentRequest`/`UpdateAgentRequest`**: BreakingChange é intent de SNAPSHOT (publish), não de DEFINITION. Misturar em request DTO criaria ambiguidade no path PUT que faz auto-snapshot. `PublishVersionAsync` é o caminho explícito; endpoint POST vem em Phase 3.A.
5. **`BreakingChange=null` tratado como `false`** (default propaga): permitiria workflow pinado em version legacy receber breaking change não-declarada. Conservadorismo (null = breaking) é mais seguro.
6. **Layer reference Persistence → Audit em vez de Persistence → Observability**: emitir audit `AgentVersionLosslessRoundtripFailed` em `Deserialize` exigiria Audit no path estático, layer violation maior. Constant declarada; emissão via dispatcher (futuro), audit no `PublishVersionAsync` que já tem `IAdminAuditLogger`.

## Consequências

- DBs antigos: ALTER idempotente em `agent_versions` adiciona `BreakingChange BOOLEAN NULL` + `SchemaVersion INTEGER NOT NULL DEFAULT 1`. Metadata-only PG 11+ (sem rewrite). Index parcial criado com `IF NOT EXISTS`.
- Workflows existentes sem `AgentVersionId` continuam funcionando — path inalterado.
- `AgentDefinition` JSON antigo (sem novos campos) deserializa: `Tools=null` aciona path legacy em `ToDefinition()`.
- Cache de AgentVersion não muda — chave é por VersionId, snapshot imutável.
- Idempotência por ContentHash preservada: AppendAsync continua retornando existing quando hash bate.

## Migration safety

- `ADD COLUMN IF NOT EXISTS` com defaults é metadata-only no PG 11+.
- Index parcial `WHERE BreakingChange=TRUE` cobre o predicate hot path; criação `IF NOT EXISTS` não bloqueia.
- Backfill manual via DBA script disponível no runbook quando exigido (versions legacy → v2 lossless).

## Phase 2 (próxima)

- `Sharing:MandatoryPin=true` + tenant-staged rollout.
- Auto-pin lazy de workflows legados no first AgentFactory call.
- `AgentService.PublishVersionAsync` integrado ao endpoint `POST /api/agents/{id}/versions`.
- `WorkflowValidator` rejeita save de workflow sem pin quando flag está on.

## Phase 3 (futuras)

- UI flow de migration: notification bell ao detectar pin ancestor de breaking; modal de diff com `ChangeReason` consolidado.
- `WorkflowAgentVersionStatus` endpoint pra UI listar pins desatualizados.
- Versionamento explícito de breaking (semver-like) — talvez mais granular que bool.

## Cleanup pré-prod (2026-05-02)

Como o projeto não foi pra produção, o discriminator `SchemaVersion=1 (lossy) | SchemaVersion=2 (lossless)` foi descartado — **v1 final é o que era v2 lossless**, único schema. Mudanças:

- `SchemaVersion` (record + coluna SQL) **removido**.
- `BreakingChange`: `bool? null` → `bool false` (default = patch). Versions sem intent declarado ficam patch implícito.
- `ToolFingerprints` record + lista descartados — `Tools` (lossless) é canonical.
- Flags `Sharing:MandatoryPin`, `Sharing:MandatoryPinTenants`, `Sharing:LosslessAgentVersion` **removidas**. Pin é mandatório global; rollout staged não faz mais sentido fora de produção.
- `IWorkflowAutoPinService` deletado — auto-pin lazy era opt-in via `MandatoryPin`. Substituído por `WorkflowService.ResolveDefaultPinsAsync`, que resolve `current` automaticamente quando o caller omite pin.
- Strategy `legacy_fallback` no `AgentFactory` removida (não há mais snapshot v1 lossy a resolver).
- Audit constant `workflow.agent_version_auto_pinned` + métrica `WorkflowAgentVersionAutoPins` removidas.
- Dados existentes regenerados via script descartável `src/EfsAiHub.Migrations.PinningV1/` (deletado do repo após sucesso).
