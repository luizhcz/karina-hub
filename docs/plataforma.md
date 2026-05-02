# Plataforma — Guia Completo

> Tudo que não é agente, workflow ou AG-UI: projetos, multi-tenancy, identidade, analytics, infraestrutura, domínio de negócio e serviços de background.

---

## Sumário

1. [Projetos](#1-projetos)
2. [Multi-Tenancy e Identidade](#2-multi-tenancy-e-identidade)
3. [Pipeline de Middleware HTTP](#3-pipeline-de-middleware-http)
4. [Data Protection (Criptografia de Credenciais)](#4-data-protection-criptografia-de-credenciais)
5. [Budget por Projeto](#5-budget-por-projeto)
6. [Model Pricing](#6-model-pricing)
7. [Analytics e Reporting](#7-analytics-e-reporting)
8. [Token Usage](#8-token-usage)
9. [Tool Invocation Tracking](#9-tool-invocation-tracking)
10. [Health Checks e Status do Sistema](#10-health-checks-e-status-do-sistema)
11. [Document Intelligence](#11-document-intelligence)
12. [Domínio Trading](#12-domínio-trading)
13. [Custom Functions (Catálogo)](#13-custom-functions-catálogo)
14. [Conversações (API Admin)](#14-conversações-api-admin)
15. [Execuções (API Admin)](#15-execuções-api-admin)
16. [Agent Sessions](#16-agent-sessions)
17. [Cross-Node Coordination](#17-cross-node-coordination)
18. [Background Services e BackgroundServiceRegistry](#18-background-services-e-backgroundserviceregistry)
19. [Database Schema](#19-database-schema)
20. [Pools de Conexão PostgreSQL](#20-pools-de-conexão-postgresql)
21. [Redis (Padrões de Uso)](#21-redis-padrões-de-uso)
22. [Observabilidade (Métricas e Tracing)](#22-observabilidade-métricas-e-tracing)
23. [Configuração Global](#23-configuração-global)
24. [API de Introspecção](#24-api-de-introspecção)

---

## 1. Projetos

### Modelo

```
src/EfsAiHub.Core.Abstractions/Projects/Project.cs
```

```csharp
public class Project
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string TenantId { get; init; }
    public string? Description { get; set; }
    public ProjectSettings Settings { get; set; } = new();
    public ProjectLlmConfig? LlmConfig { get; set; }
    public JsonDocument? Budget { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
}
```

### ProjectSettings

```
src/EfsAiHub.Core.Abstractions/Projects/ProjectSettings.cs
```

| Propriedade | Tipo | Default | Descrição |
|-------------|------|---------|-----------|
| `DefaultProvider` | string? | null | Provider LLM padrão (ex: "OPENAI") |
| `DefaultModel` | string? | null | Modelo padrão (ex: "gpt-4o") |
| `DefaultTemperature` | float? | null | Temperatura padrão |
| `MaxTokensPerDay` | int? | null | Limite diário de tokens |
| `MaxCostUsdPerDay` | decimal? | null | Limite diário de custo USD |
| `MaxConcurrentExecutions` | int? | null | Execuções simultâneas |
| `MaxRequestsPerMinute` | int? | null | RPM rate limit |
| `MaxConversationsPerUser` | int? | null | Conversas por usuário |
| `HitlEnabled` | bool | true | HITL habilitado |
| `BackgroundResponsesEnabled` | bool | true | Background responses habilitado (reservado) |
| `MaxSandboxTokensPerDay` | int? | 50000 | Limite sandbox diário |

### ProjectLlmConfig

```
src/EfsAiHub.Core.Abstractions/Projects/ProjectLlmConfig.cs
```

```csharp
public record ProjectLlmConfig(
    Dictionary<string, ProviderCredentials> Credentials,  // "OPENAI" → creds
    string? DefaultModel,
    string? DefaultProvider
);

public record ProviderCredentials(
    string? ApiKey,       // Plaintext no domínio (criptografado no banco)
    string? Endpoint,
    string? KeyVersion    // ISO-8601 timestamp da criptografia
);
```

### API REST

```
src/EfsAiHub.Host.Api/Controllers/ProjectsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/projects` | Criar projeto |
| GET | `/api/projects` | Listar projetos (filtrado por tenant) |
| GET | `/api/projects/{id}` | Obter projeto |
| PUT | `/api/projects/{id}` | Atualizar projeto |
| DELETE | `/api/projects/{id}` | Deletar projeto (bloqueia "default") |

**Segurança na resposta:** A API **nunca** retorna a API key. Retorna apenas `apiKeySet: bool` e `keyVersion` (timestamp).

### Model Catalog

```
src/EfsAiHub.Core.Abstractions/Projects/ModelCatalog.cs
```

Catálogo de modelos disponíveis por provider:

```csharp
public class ModelCatalog
{
    public required string Id { get; init; }          // Model ID
    public required string Provider { get; init; }     // Provider name
    public required string DisplayName { get; set; }
    public string? Description { get; set; }
    public int? ContextWindow { get; set; }
    public List<string> Capabilities { get; set; } = [];  // "chat", "vision", "function_calling"
    public bool IsActive { get; set; } = true;
}
```

**Controller:** `ModelCatalogController` — CRUD em `/api/admin/model-catalog`

---

## 2. Multi-Tenancy e Identidade

### Hierarquia de Contexto

```
Tenant (x-efs-tenant-id)
  └─ Project (x-efs-project-id)
       └─ User (x-efs-account | x-efs-user-profile-id)
```

### TenantContext

```
src/EfsAiHub.Core.Abstractions/Identity/TenantContext.cs
```

```csharp
public sealed class TenantContext
{
    public string TenantId { get; }
    public TenantIsolationLevel IsolationLevel { get; }  // Shared | Dedicated
    public static TenantContext Default { get; } = new("default");
}
```

Resolvido pelo `TenantMiddleware` a partir do header `x-efs-tenant-id`. Se ausente, usa `"default"`.

### ProjectContext

```
src/EfsAiHub.Core.Abstractions/Identity/ProjectContext.cs
```

```csharp
public sealed class ProjectContext
{
    public string ProjectId { get; }
    public string? ProjectName { get; }
    public static ProjectContext Default { get; } = new("default", "Default");
}
```

Resolvido pelo `ProjectMiddleware` com prioridade:
1. Header `x-efs-project-id`
2. JWT claim `project_id`
3. Route parameter `projectId`
4. Fallback: `"default"`

**Implementação:** Singleton com `AsyncLocal` — flui automaticamente pelo contexto async.

### UserIdentity

```
src/EfsAiHub.Host.Api/Middleware/Identity/UserIdentityResolver.cs
```

Resolvido dos headers:
- `x-efs-account` → `UserType = "cliente"`
- `x-efs-user-profile-id` → `UserType = "assessor"`

**Regras:** Exatamente um header deve estar presente; ambos ou nenhum = erro.

### AdminGateMiddleware

```
src/EfsAiHub.Host.Api/Middleware/AdminGateMiddleware.cs
```

Controla acesso a endpoints administrativos:

**Rotas públicas (sem admin):**
- `/api/chat/ag-ui/*`
- `POST /api/workflows`, `PUT /api/workflows/{id}`
- `POST /api/agents`, `PUT /api/agents/{id}`
- `/api/conversations/*`, `/api/users/*/conversations`
- `GET /api/projects`, `GET /api/projects/{id}`
- `GET /api/enums`

**Tudo mais:** Requer que o userId esteja em `Admin:AccountIds`.

### Trust Boundary e Requisitos de Deploy

O sistema é projetado para rodar **atrás de um API Gateway** (Azure APIM, Kong, AWS API Gateway, etc.) que funciona como ponto de autenticação e autorização. A cadeia de confiança:

1. **API Gateway** valida JWT/OAuth2 do usuário final
2. **Gateway injeta headers** `x-efs-account`, `x-efs-user-profile-id`, `x-efs-tenant-id`, `x-efs-project-id` na requisição
3. **Backend confia nos headers** — não re-valida o JWT (evita duplicação de lógica e latência)
4. **AdminGateMiddleware** funciona como segunda camada de autorização (valida se o userId é admin)

**Requisitos de produção:**
- O backend **não deve** receber tráfego direto da internet sem Gateway
- O Gateway deve **strip** qualquer header `x-efs-*` enviado pelo cliente antes de injetar os seus
- Em ambientes de desenvolvimento, headers podem ser enviados diretamente (sem Gateway) para facilitar testes
- O `ProjectMiddleware` suporta JWT claim `project_id` como fallback para cenários onde o Gateway injeta via token

**Gate desabilitado:** Se `AccountIds` está vazio (modo dev/test).

### Admin Audit Trail

Tabela `aihub.admin_audit_log` registra toda mudança CRUD em recursos administrativos — pré-requisito de compliance. Cada controller admin grava uma linha após persistir:

| Controller | Ações auditadas | ResourceType |
|---|---|---|
| `ProjectsController` | create / update / delete | `project` |
| `AgentsController` | create / update / delete | `agent` |
| `WorkflowsController` | create / update / delete | `workflow` |
| `SkillsController` | upsert (create/update) / delete | `skill` |
| `ModelPricingController` | upsert (create/update) / delete | `model_pricing` |

**Schema:**

```sql
CREATE TABLE aihub.admin_audit_log (
    "Id"            BIGINT IDENTITY PRIMARY KEY,
    "TenantId"      VARCHAR(128) NULL,
    "ProjectId"     VARCHAR(128) NULL,
    "ActorUserId"   VARCHAR(128) NOT NULL,     -- UserId do header
    "ActorUserType" VARCHAR(32)  NULL,         -- 'cliente' | 'assessor'
    "Action"        VARCHAR(64)  NOT NULL,     -- create | update | delete | actions namespaced (ver tabela abaixo)
    "ResourceType"  VARCHAR(64)  NOT NULL,
    "ResourceId"    VARCHAR(128) NOT NULL,
    "PayloadBefore" JSONB        NULL,         -- snapshot pré (update/delete)
    "PayloadAfter"  JSONB        NULL,         -- snapshot pós (create/update)
    "Timestamp"     TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
```

**Campos indexados:**
- `(TenantId, Timestamp DESC)` — governance dashboard "o que mudou no tenant X nas últimas 24h"
- `(ResourceType, ResourceId)` — histórico de um recurso específico
- `(ActorUserId, Timestamp DESC)` — compliance "minhas ações administrativas"

**Semântica:**

- **Fire-and-log:** `IAdminAuditLogger.RecordAsync()` engole exceções — falha de write de auditoria **não** quebra o CRUD primário. Log warning no service logger com tag `[AdminAudit]`.
- **Actor:** `UserIdentityResolver` extrai dos headers `x-efs-account` / `x-efs-user-profile-id`. Actor default `"system:anonymous"` quando headers ausentes.
- **Retenção:** `AuditRetentionService` faz DELETE batched (1000 rows / batch, sleep 200ms) via `AuditRetentionDays` (default 90).
- **Consulta:** `GET /api/admin/audit-log` — paginado, ordena `Timestamp DESC`. `TenantId` é sempre aplicado do contexto da request (não do query param — impede cross-tenant scan).

**UI:** `/audit/admin` (sidebar → Admin → "Audit Admin"). Modal de detalhe exibe `PayloadBefore/After` via `JsonViewer` para diff visual. Filtro `Ação` na página whitelista as actions canônicas.

**Ações canônicas (`AdminAuditActions`):**

| Action | Emitida por | Payload |
|---|---|---|
| `create` / `update` / `delete` | CRUD genérico (project, agent, workflow, skill, model_pricing) | snapshot before/after do recurso |
| `agent.visibility_changed` | `PATCH /api/agents/{id}/visibility` | `{ before: { visibility }, after: { visibility } }` |
| `workflow.visibility_changed` | `PATCH /api/workflows/{id}/visibility` | `{ before: { visibility }, after: { visibility } }` |
| `cross_project_invoke` | `AgentFactory` quando workflow caller invoca agent global de outro project | `{ callerProjectId, ownerProjectId, workflowId, agentId }` (LRU 60s throttle) |
| `agent.version_published` | `POST /api/agents/{id}/versions` em publish efetivo (idempotente por ContentHash) | `{ revision, breakingChange, changeReason, contentHash }` |
| `agent.version_lossless_roundtrip_failed` | declarada — emissão futura via dispatcher | `{ agentVersionId, agentDefinitionId, contentHash }` |
| `workflow.agent_version_pinned` | `PATCH /api/workflows/{id}/agents/{agentId}/pin` | `{ agentId, previousVersionId, newVersionId, wasBreaking, reason }` |

### Pinning Federated — endpoints

Endpoints novos do épico Pinning Federated (Fase 3 — UI flow). Detalhamento de algoritmo + decisões em [docs/agentes.md § Pinning Lossless + Patch Propagation](agentes.md#pinning-lossless--patch-propagation) e [ADR 0018](adr/0018-lossless-agent-version-pinning.md).

| Método | Rota | Descrição | Audit |
|---|---|---|---|
| `POST` | `/api/agents/{id}/versions` | Publica nova `AgentVersion` snapshot lossless. Body `{ breakingChange, changeReason }`. `breakingChange=true` exige `changeReason` non-blank. Idempotente por ContentHash — sem mudança no conteúdo retorna existing. | `agent.version_published` em publish efetivo |
| `GET` | `/api/workflows/{id}/agent-version-status` | Retorna `WorkflowAgentVersionStatusResponse[]` por `AgentReference` do workflow: `{ agentId, agentName, pinnedVersionId, pinnedRevision, currentVersionId, currentRevision, isPinnedBlockedByBreaking, hasUpdate, changes[] }`. `changes[]` enumera revisions entre pin e current via `IAgentVersionRepository.ListBetweenRevisionsAsync`. | (sem audit — leitura) |
| `PATCH` | `/api/workflows/{id}/agents/{agentId}/pin` | Atualiza `WorkflowAgentReference.AgentVersionId`. Body `{ newVersionId, reason? }` (`newVersionId` é required; `reason` opcional pra contexto do audit). Validação confirma `pinned.AgentDefinitionId == agentId`. | `workflow.agent_version_pinned` |
| `GET` | `/api/notifications/agent-breaking-changes?days={N}` | Lista `AgentBreakingChangeNotification[]` (cap 50) com `BreakingChange=true` publicadas nos últimos N dias (1-90, default 7), ordenadas `CreatedAt DESC`. Visibility respeita `agent_definitions.HasQueryFilter` por tenant + project. `[ResponseCache(Duration=60, Location=Client)]` no controller. Alimenta `NotificationBell` no Header. | (sem audit — leitura) |

**Sharing flags relacionadas (via `IOptionsMonitor<SharingOptions>`):**

| Flag | Default | Efeito |
|---|---|---|
| `Sharing:Enabled` | `true` | Master switch — `false` esconde toggles de visibility e ignora `Visibility=global` em listagens. |
| `Sharing:CrossProjectEnabled` | `true` | `false` → `WorkflowValidator` rejeita refs cross-project; runtime rejeita resolução cross-project. |
| `Sharing:WhitelistEnabled` | `true` | `false` → `AgentDefinition.AllowedProjectIds` é ignorado (whitelist relaxada). |
| `Sharing:AuditCrossInvoke` | `true` | `false` → skip audit `cross_project_invoke` (mantém métrica + log). |
| `Sharing:CrossInvokeAuditThrottleSeconds` | `60` | Janela LRU pra throttle do audit `cross_project_invoke`. |

> Pin de agent version é **mandatório global** — não há flag de opt-out (sub-prod cleanup descartou `MandatoryPin`/`MandatoryPinTenants`/`LosslessAgentVersion`). `WorkflowService.ResolveDefaultPinsAsync` resolve `current` automaticamente quando o caller omite pin.

### MCP Servers Registry

Tabela `aihub.mcp_servers` centraliza os servidores MCP (Model Context Protocol) conhecidos pela plataforma. Substitui a configuração inline que vivia dentro de `agent_definitions.Data` — agents agora referenciam por `McpServerId` e o provider LLM resolve `ServerLabel`/`ServerUrl`/`AllowedTools`/`Headers` em runtime (**id-based live resolution**).

**Schema:**

```sql
CREATE TABLE aihub.mcp_servers (
    "Id"        VARCHAR(128) PRIMARY KEY,
    "Name"      VARCHAR(256) NOT NULL,
    "Data"      JSONB        NOT NULL,   -- McpServer serializado
    "ProjectId" VARCHAR(128) NOT NULL DEFAULT 'default',
    "CreatedAt" TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    "UpdatedAt" TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE INDEX "IX_mcp_servers_ProjectId_Name" ON aihub.mcp_servers ("ProjectId", "Name");
```

**Escopo:** project-scoped via `HasQueryFilter(e => e.ProjectId == CurrentProjectId)` no DbContext. Um MCP cadastrado em um projeto não é visível em outro.

**Fluxo de resolução (runtime):**
1. `AzureFoundryClientProvider.CreateAgentAsync` chama `FoundryToolBuilder.BuildAsync`.
2. Para cada tool com `Type=mcp`: se `McpServerId != null`, busca no `IMcpServerRepository`. Se não achar (dangling após delete), log warning e tool é pulada.
3. Monta `MCPToolDefinition(ServerLabel, ServerUrl)` + `AllowedTools` com os campos do registro — **não do agent**.

**Endpoint CRUD:** `POST/GET/PUT/DELETE /api/admin/mcp-servers` (admin-gated). Cada escrita emite linha em `admin_audit_log` com `resourceType=mcp_server` (ver seção anterior).

**Sem validação de rede:** create/update NÃO faz health check do MCP — cadastrar MCPs offline é permitido. A classe `McpHealthChecker` foi **removida** nesta feature. Detalhes completos em [`docs/mcp.md`](./mcp.md).

### DefaultProjectGuard

```
src/EfsAiHub.Host.Api/Middleware/DefaultProjectGuard.cs
```

Bloqueia acesso não-admin ao projeto `"default"`, exceto para rotas exemptadas (agents, workflows, AG-UI).

---

## 3. Pipeline de Middleware HTTP

```
src/EfsAiHub.Host.Api/Program.cs
```

```
HttpRequest
    ↓
CORS
    ↓
SecurityHeadersMiddleware         — Headers de segurança
    ↓
TenantMiddleware                  — Resolve x-efs-tenant-id
    ↓
ProjectMiddleware                 — Resolve x-efs-project-id (header → JWT → route → default)
    ↓
DefaultProjectGuard               — Bloqueia não-admin no projeto "default"
    ↓
ProjectRateLimitMiddleware        — Rate limit por projeto (429)
    │                               + Budget guard (402)
    │                               Redis sliding window, fail-open
    ↓
Authorization
    ↓
AdminGateMiddleware               — Gate endpoints admin-only (403)
    ↓
Controllers / Endpoints
```

**A ordem é crítica:** Tenant → Project → Guard → Rate Limit → Auth → Admin Gate.

---

## 4. Data Protection (Criptografia de Credenciais)

### Setup

```csharp
// Program.cs
var dpBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("EfsAiHub")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(730));  // 2 anos

if (redisMultiplexer is not null)
    dpBuilder.PersistKeysToStackExchangeRedis(redisMultiplexer, "DataProtection:Keys");
```

### Mecanismo

| Aspecto | Detalhe |
|---------|---------|
| **Framework** | ASP.NET Core Data Protection API |
| **Protector name** | `"ProjectLlmCredentials"` |
| **Key lifetime** | 730 dias (2 anos) |
| **Key ring storage** | Redis em `DataProtection:Keys` |
| **Fallback** | Ephemeral (memória) se Redis indisponível |

### Fluxo de Criptografia

```
PgProjectRepository.CreateAsync() / UpdateAsync()
    ├─ Para cada credential com ApiKey não vazio:
    │   ├─ protector.Protect(plainKey) → apiKeyCipher
    │   └─ KeyVersion = DateTime.UtcNow.ToString("O")
    └─ Armazena como JSONB no banco

PgProjectRepository.GetByIdAsync()
    ├─ Para cada credential com apiKeyCipher:
    │   ├─ protector.Unprotect(apiKeyCipher) → plainKey
    │   └─ Se falhar (key rotated): ApiKey = null (graceful)
    └─ Retorna plaintext ao domínio
```

**Segurança:** API keys nunca retornadas na API REST — apenas `apiKeySet: bool`.

---

## 5. Budget por Projeto

### ProjectBudgetGuard

```
src/EfsAiHub.Platform.Runtime/Guards/ProjectBudgetGuard.cs
```

Enforcement diário por projeto usando Redis:

| Limite | Chave Redis | TTL |
|--------|-------------|-----|
| Tokens/dia | `budget:tokens:{projectId}:{yyyy-MM-dd}` | 48h |
| Custo USD/dia | `budget:cost:{projectId}:{yyyy-MM-dd}` | 48h |

### Fluxo

```
Request chega →
    ProjectRateLimitMiddleware →
        ProjectBudgetGuard.CheckAsync() →
            ├─ Lê ProjectSettings do banco
            ├─ Verifica tokens acumulados vs MaxTokensPerDay
            ├─ Verifica custo acumulado vs MaxCostUsdPerDay
            └─ Se excedido → LogCritical + métrica `llm.budget.exceeded{scope=project}` (não bloqueia)

Após cada chamada LLM →
    TokenUsagePersistenceService →
        ProjectBudgetGuard.IncrementAsync() →
            ├─ INCR budget:tokens:{projectId}:{date}
            └─ INCRBYFLOAT budget:cost:{projectId}:{date}
```

**Fail-open:** Se Redis indisponível, loga warning e permite.

### ⚠️ Semântica: SOFT budget

O `ProjectBudgetGuard` implementa **soft budget** — não hard budget. Consequências práticas:

- O check **bloqueia** apenas requisições *novas* depois que o contador Redis já ultrapassou o teto.
- O incremento é **post-LLM-call** (via `TokenUsagePersistenceService`), não upfront.
- Requisições concorrentes próximas do limite podem passar juntas no check, rodar em paralelo e — somadas — exceder o limite em alguns pontos percentuais.

**Por que isso importa:** o label visível na UI ("Limite diário") pode sugerir comportamento *hard* para o usuário final. Alinhar com Product Owner antes de comunicar como contrato SLA.

**Migração para HARD budget** (pendente de decisão do PO — ver [issue #1](https://github.com/luizhcz/efs-ai-hub/issues/1)):

1. Estimar `max_tokens` da request antes da chamada LLM.
2. Reservar via Lua script atômico (`INCR` + comparar + rollback se exceder).
3. Executar chamada LLM.
4. Reconciliar contador com tokens reais consumidos.

Esforço estimado: ~1 semana. Decisão depende de requisito comercial (contratos enterprise geralmente exigem hard).

---

## 6. Model Pricing

### Modelo

```
src/EfsAiHub.Core.Abstractions/Observability/ModelPricing.cs
```

```csharp
public class ModelPricing
{
    public int Id { get; set; }
    public required string ModelId { get; init; }
    public required string Provider { get; init; }
    public decimal PricePerInputToken { get; set; }
    public decimal PricePerOutputToken { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
```

### API REST

```
src/EfsAiHub.Host.Api/Controllers/ModelPricingController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/admin/model-pricing` | Listar todos os preços |
| GET | `/api/admin/model-pricing/{id}` | Obter preço por ID |
| POST | `/api/admin/model-pricing` | Upsert preço (invalida cache) |
| DELETE | `/api/admin/model-pricing/{id}` | Deletar preço |
| POST | `/api/admin/model-pricing/refresh-view` | Refresh manual da matview `v_llm_cost` |

### Cache (Three-Tier)

```
src/EfsAiHub.Platform.Runtime/Execution/ModelPricingCache.cs
```

```
L1: In-Memory → L2: Redis → L3: PostgreSQL
```

- `GetAsync(modelId)` — busca pricing ativo (EffectiveFrom ≤ now, EffectiveTo null ou ≥ now)
- `InvalidateAsync(modelId?)` — limpa cache (chamado após upsert/delete)
- Usado pelo `TokenTrackingChatClient` para calcular custo em tempo real

### Seed

`DatabaseBootstrapService` — popula tabela de precos se vazia no startup a partir de `backups/model-pricing.json`.

---

## 7. Analytics e Reporting

### API REST

```
src/EfsAiHub.Host.Api/Controllers/AnalyticsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/analytics/executions/summary` | Métricas agregadas (success rate, P50/P95 latência) |
| GET | `/api/analytics/executions/timeseries` | Série temporal por hora/dia |

**Query params:** `from`, `to` (default: últimos 30 dias), `workflowId`, `groupBy` (hour/day).

### Materialized Views

Refreshed a cada 5 minutos pelo `LlmCostRefreshService` (com advisory lock para leader election):

| View | Propósito | Agrupamento |
|------|-----------|-------------|
| `v_llm_cost` | Custo estimado por chamada LLM | Por chamada |
| `mv_execution_stats_hourly` | Estatísticas de execução | Por hora + workflow + status |
| `mv_token_usage_hourly` | Token usage agregado | Por hora + agente + modelo |

**Cálculo de custo:** LATERAL join na tabela `model_pricing` para encontrar pricing ativo na data da chamada.

**Métricas disponíveis:**
- Total de execuções, taxa de sucesso
- P50/P95 de latência (via `percentile_cont`)
- Tokens input/output/total por agente e modelo
- Custo estimado em USD

---

## 8. Token Usage

### Modelo

```
src/EfsAiHub.Core.Abstractions/Observability/LlmTokenUsage.cs
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `AgentId` | string | Agente que fez a chamada |
| `ModelId` | string | Modelo LLM |
| `ExecutionId` | string? | Execução do workflow |
| `InputTokens` | int | Tokens de entrada |
| `OutputTokens` | int | Tokens de saída |
| `TotalTokens` | int | Total |
| `DurationMs` | double | Duração da chamada |
| `PromptVersionId` | string? | Versão do prompt |
| `OutputContent` | string? | Output truncado (4000 chars) |
| `RetryCount` | int | Retries antes do sucesso |

### Persistência em Batch

```
src/EfsAiHub.Host.Worker/Services/TokenUsagePersistenceService.cs
```

- Bounded Channel (capacidade 1000, DropOldest)
- Batch de até 10 itens por flush
- Após persistir, incrementa budget do projeto via `ProjectBudgetGuard.IncrementAsync()`

### API REST

```
src/EfsAiHub.Host.Api/Controllers/TokenUsageController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/token-usage/summary` | Resumo global por agente |
| GET | `/api/token-usage/agents/{agentId}/summary` | Resumo por agente |
| GET | `/api/token-usage/agents/{agentId}/history` | Histórico detalhado (limit 1-200) |
| GET | `/api/token-usage/executions/{executionId}` | Tokens por execução |
| GET | `/api/token-usage/throughput` | Throughput horário (últimas 24h) |
| GET | `/api/token-usage/workflows/summary` | Tokens + custo por workflow |
| GET | `/api/token-usage/projects/summary` | Tokens + custo por projeto |

---

## 9. Tool Invocation Tracking

### Modelo

```
src/EfsAiHub.Core.Abstractions/Observability/ToolInvocation.cs
```

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `ExecutionId` | string | Execução do workflow |
| `AgentId` | string | Agente que invocou |
| `ToolName` | string | Nome da tool |
| `Arguments` | string? | Args JSON |
| `Result` | string? | Resultado (max 500 chars) |
| `DurationMs` | double | Duração |
| `Success` | bool | Sucesso ou falha |
| `ErrorMessage` | string? | Mensagem de erro |

### Persistência em Batch

```
src/EfsAiHub.Host.Worker/Services/ToolInvocationPersistenceService.cs
```

Mesmo padrão do TokenUsagePersistenceService: Channel bounded (1000), batch de 10, DropOldest.

---

## 10. Health Checks e Status do Sistema

### Endpoints de Saúde

| Path | Descrição | Retorno |
|------|-----------|---------|
| `GET /health/live` | Liveness probe (processo vivo) | Sempre 200 |
| `GET /health/ready` | Readiness probe (Postgres + Redis) | 200 ou 503 |

### SystemController

```
src/EfsAiHub.Host.Api/Controllers/SystemController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/system/health/circuit-breakers` | Estado dos circuit breakers LLM |

**Circuit Breaker Response:**
```json
{
  "circuitBreakers": [
    {
      "providerKey": "OPENAI",
      "status": "Closed",
      "consecutiveFailures": 0,
      "isOperational": true
    }
  ]
}
```

---

## 11. Document Intelligence

Integração com o [Azure AI Document Intelligence](https://learn.microsoft.com/azure/ai-services/document-intelligence/) para extração estruturada de texto, tabelas e metadados de documentos PDF.

### Modelos de domínio

```
src/EfsAiHub.Core.Agents/DocumentIntelligence/
```

| Classe | Propósito |
|--------|-----------|
| `ExtractionJob` | Job de extração com status tracking (created → running → succeeded/failed/cached) |
| `ExtractionRequest` | Input: DocumentSource, Model, Features, CacheEnabled |
| `ExtractionEvent` | Evento de auditoria por etapa (source_validated, file_validated, cache_hit/miss, gate_*, di_*, completed, failed) |
| `ExtractionCacheEntry` | Dedup por SHA-256 do conteúdo + modelo + features |
| `DocumentSource` | Suporta dois modos: `blobUrl` (URL HTTP/HTTPS) ou `bytes` (base64 inline) |

### Interface do serviço

```
src/EfsAiHub.Core.Agents/DocumentIntelligence/IDocumentIntelligenceService.cs
```

| Método | Descrição |
|--------|-----------|
| `AnalyzeAsync(Uri, model, features, ct)` | Envia documento por URL — Azure DI faz download do documento |
| `AnalyzeBytesAsync(byte[], model, features, ct)` | Envia documento como bytes — upload direto via `Base64Source` do SDK |

Ambos retornam `DiAnalyzeResult` contendo:
- `Content` — texto extraído em plain text (usado como input para agentes downstream)
- `RawJson` — resposta completa do Azure DI (armazenada comprimida no Redis)
- `PageCount`, `HasTables`, `HasHandwriting`, `PrimaryLanguage`, `DurationMs`

### Error Codes

```csharp
public static class ExtractionErrorCode
{
    FILE_SIZE_EXCEEDED,   // Arquivo excede MaxFileSizeBytes
    UNREADABLE_PDF,       // PDF corrompido, magic bytes ausentes, ou base64 inválido
    SOURCE_UNAVAILABLE,   // URL inacessível, bytes vazios, ou tipo de source não suportado
    AZURE_DI_FAILURE,     // Erro do Azure DI (500, 401, 403, 429)
    TIMEOUT,              // Timeout de polling do Azure DI
    GATE_TIMEOUT,         // Timeout aguardando semaphore gate
    CANCELLED             // Cancelado pelo workflow
}
```

### Executor

```
src/EfsAiHub.Platform.Runtime/Functions/DocumentIntelligenceFunctions.cs
```

Registrado como `document_intelligence` no `ICodeExecutorRegistry`. Fluxo de execução:

1. **Resolve source** → download (blobUrl) ou decode base64 (bytes)
2. **Validação local** — verifica magic bytes `%PDF` e tamanho máximo (`MaxFileSizeBytes`, padrão 50 MB)
3. **SHA-256 hash** — calcula hash do conteúdo para cache (inclui `outputFormat` no `features_hash`)
4. **Cache check** — busca no PostgreSQL (ExtractionCacheEntry) + Redis (content armazenado)
5. **Semaphore gate** — controla concorrência (1 requisição por vez, configurable via `GateWaitTimeoutSeconds`)
6. **Azure DI call** — `AnalyzeAsync` (URL) ou `AnalyzeBytesAsync` (bytes direto) com timeout configurável. `OutputContentFormat` passado pro SDK conforme `request.outputFormat` (default `markdown`).
7. **Redis storage** — armazena resultado gzipado (`:full`), content no formato escolhido (`:content`), e metadados (`:meta`). Cache key (`di:v2:{sha}:{model}:{format}`) inclui format para não confundir markdown com plain text.
8. **Cache upsert** — persiste entrada no PostgreSQL para futuras consultas
9. **Cost tracking** — resolve preço por página consultando `aihub.document_intelligence_pricing` (via cache em memória + Redis + Postgres, ver §20.2). Fallback hardcoded se DB vazio. Integra com `ExecutionBudget`.

Características:
- **Output em markdown por default** desde 2026 — entrega tabelas/headers prontos para o LLM consumir. Flag `outputFormat: "text"` reverte para plain text se algum caller precisar.
- **Dois modos de envio**: URL (Azure DI faz download) ou bytes (upload direto via `Base64Source`)
- **Cache em 3 camadas**: Redis `:full` (gzip), `:content` (markdown ou text), `:meta` (JSON metadados)
- **Autenticação dual**: API Key ou Managed Identity (configurável via `UseManagedIdentity`)

### Pricing

Tabela dedicada `aihub.document_intelligence_pricing` — ver [§20.2](#202-pricing-document-intelligence) para detalhes e seed oficial.

### Configuração

```
src/EfsAiHub.Platform.Runtime/Options/DocumentIntelligenceOptions.cs
```

| Propriedade | Default | Descrição |
|-------------|---------|-----------|
| `Endpoint` | — | Azure DI endpoint |
| `UseManagedIdentity` | true | Usar Managed Identity (false = API Key) |
| `ApiKey` | null | Chave de API (obrigatória se `UseManagedIdentity=false`) |
| `DefaultModel` | `prebuilt-layout` | Modelo padrão do Azure DI |
| `MaxFileSizeBytes` | 50 MB | Tamanho máximo do arquivo PDF em bytes |
| `PollingTimeoutSeconds` | 180 | Timeout de polling Azure DI |
| `GateWaitTimeoutSeconds` | 600 | Timeout do semaphore gate |
| `CacheTtlDays` | 7 | TTL do cache Redis |

### Configuração Docker (docker-compose.yml)

Para rodar localmente em Docker, o backend precisa das seguintes variáveis de ambiente (sem Managed Identity):

```yaml
DocumentIntelligence__Endpoint: ${DOCUMENT_INTELLIGENCE_ENDPOINT}
DocumentIntelligence__ApiKey: ${DOCUMENT_INTELLIGENCE_API_KEY}
DocumentIntelligence__UseManagedIdentity: "false"
```

---

## 12. Domínio Trading

### Modelos de Saída

```
src/EfsAiHub.Core.Agents/Trading/
```

| Modelo | Campos Principais |
|--------|-------------------|
| `OutputBoleta` | BoletaList, Message, Command, UiComponent |
| `Boleta` | OrderType, Ticker, Account, Quantity, PriceLimit, PriceType (M/L/F), Volume, ExpireTime |
| `OutputRelatorio` | Message, Posicoes (PosicaoCliente[]), UiComponent |
| `OutputAtendimento` | ResponseType ("boleta"/"relatorio"/"texto"/"recomendacao"), campos condicionais |
| `BoletaColetaOutput` | Pronto (bool), MensagemUsuario, CamposColetados, CamposFaltantes |
| `AgentOutputEnvelope` | ResponseType, Message, UiComponent, Payload (JsonElement), EnrichmentMetadata |

### Ativo (Ação/Papel)

```csharp
public class Ativo
{
    public required string Ticker { get; init; }
    public required string Nome { get; set; }
    public string? Setor { get; set; }
    public string? Descricao { get; set; }
}
```

### Tools de Trading

```
src/EfsAiHub.Platform.Runtime/Functions/BoletaToolFunctions.cs
```

| Tool | Descrição |
|------|-----------|
| `BuscarAtivo(query, top_k)` | Busca ativos por ticker/nome/setor |
| `ObterPosicaoCliente(conta, ticker)` | Posição atual do cliente |
| `SendOrder(boletas)` | Registra validação de ordem (não executa no OMS) |
| `ConfirmBoleta()` | HITL: solicita aprovação humana via HitlService |

**AccountGuard:** Modo `ClientLocked` rejeita contas que não batem.

---

## 13. Custom Functions (Catálogo)

### Funções Registradas no ICodeExecutorRegistry

| Função | Arquivo | Descrição |
|--------|---------|-----------|
| `document_intelligence` | DocumentIntelligenceFunctions.cs | Extração OCR via Azure DI |
| `search_single` | WebSearchBatchFunctions.cs | Pesquisa web single asset |
| `atendimento_pre` | AtendimentoPreProcessor.cs | Pre-processamento: timezone, defaults, normalização numérica |
| `atendimento_post` | AtendimentoPostProcessor.cs | Pós-processamento: validação de schema por response_type |

### Funções Registradas no IFunctionToolRegistry (Agent Tools)

| Tool | Descrição |
|------|-----------|
| `BuscarAtivo` | Busca de ativos |
| `ObterPosicaoCliente` | Posição do cliente |
| `SendOrder` | Validação de ordem |
| `ConfirmBoleta` | HITL aprovação |
| `SearchWeb` | Pesquisa web (Wikipedia, DuckDuckGo, StatusInvest) |
| `ConsultarCarteira` | Consulta carteira Apex |
| `ExecutarResgate` | Executa resgate Apex |
| `ExecutarAplicacao` | Executa aplicação Apex |
| `CalcularIrResgate` | Cálculo IR sobre resgate |

---

## 14. Conversações (API Admin)

```
src/EfsAiHub.Host.Api/Controllers/ConversationsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/conversations` | Criar conversa |
| GET | `/api/conversations/{id}` | Obter metadata |
| GET | `/api/conversations/{id}/messages` | Listar mensagens (paginado) |
| GET | `/api/conversations/{id}/full` | Full dump: metadata + 1000 msgs + execuções (nós, tools, eventos) |
| POST | `/api/conversations/{id}/messages` | Enviar mensagens (trigger workflow se última = user) |
| GET | `/api/conversations/{id}/messages/stream` | SSE stream de eventos |
| DELETE | `/api/conversations/{id}` | Deletar conversa + mensagens |
| DELETE | `/api/conversations/{id}/context` | Limpar contexto (reset histórico) |
| GET | `/api/admin/conversations` | Admin: listar com filtros (userId, workflowId, projectId, datas, paginação) |

### User Conversations

```
src/EfsAiHub.Host.Api/Controllers/UserConversationsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/users/{userId}/conversations` | Listar conversas do usuário (50 mais recentes) |

---

## 15. Execuções (API Admin)

```
src/EfsAiHub.Host.Api/Controllers/ExecutionsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/executions` | Listar com filtros (workflowId, status, datas, paginação) |
| GET | `/api/executions/{id}` | Detalhe da execução |
| GET | `/api/executions/{id}/full` | Full dump: metadata + nós + tools + eventos |
| DELETE | `/api/executions/{id}` | Solicitar cancelamento (202) |

---

## 16. Agent Sessions

```
src/EfsAiHub.Host.Api/Controllers/AgentSessionsController.cs
```

Chat multi-turno com agente individual (sem workflow):

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/agents/{agentId}/sessions` | Criar sessão |
| GET | `/api/agents/{agentId}/sessions` | Listar sessões |
| GET | `/api/agents/{agentId}/sessions/{sessionId}` | Obter metadata |
| DELETE | `/api/agents/{agentId}/sessions/{sessionId}` | Deletar sessão |
| POST | `/api/agents/{agentId}/sessions/{sessionId}/run` | Enviar mensagem e receber resposta |

Mantém estado da conversa entre turnos com gerenciamento automático de histórico.

**Cleanup:** `AgentSessionCleanupService` remove sessões expiradas a cada 6 horas.

---

## 17. Cross-Node Coordination

### PgCrossNodeBus

```
src/EfsAiHub.Infra.Messaging/PgCrossNodeBus.cs
```

Três canais PostgreSQL LISTEN/NOTIFY:

| Canal | Payload | Propósito |
|-------|---------|-----------|
| `efs_exec_cancel` | `{ executionId }` | Cancelar execução em outro pod |
| `efs_hitl_resolved` | `{ interactionId, resolution, approved }` | Propagar resolução HITL |
| `efs_cache_invalidate` | `{ cacheName, key, sourcePodId }` | **F2** — invalidar L1 cross-pod (persona, pricing). Ver [ADR 002](adr/002-cache-invalidation-cross-pod.md). |

**Retry:** 2 tentativas por publish, 500ms entre retries.

### CrossNodeCoordinator

```
src/EfsAiHub.Host.Worker/Services/CrossNodeCoordinator.cs
```

BackgroundService que escuta ambos os canais:
- Usa pool `"sse"` (conexão long-lived)
- Reconnect com exponential backoff: `min(30s, 1s << min(attempt, 5)) + jitter`
- **Cancel handler:** `IExecutionSlotRegistry.TryCancel()`
- **HITL handler:** `IHumanInteractionService.Resolve()` com `publishToCross=false` (previne loop)
- Idempotente: se execução/interação não está neste pod, no-op silencioso

---

## 18. Background Services e BackgroundServiceRegistry

### BackgroundServiceRegistry

Todos os background services são registrados via `IBackgroundServiceRegistry`, um singleton que mantém um `ConcurrentDictionary<string, BackgroundServiceDescriptor>` com metadata de cada serviço (nome, lifecycle, intervalo). Isso permite introspecção em runtime dos serviços ativos.

### 10 Background Services

| Service | Lifecycle | Intervalo | Propósito |
|---------|-----------|-----------|-----------|
| `DatabaseBootstrapService` | OneTime | startup | Marca execuções órfãs como Failed, expira HITLs órfãos, recarrega HITLs pendentes |
| `AgentSessionCleanupService` | Continuous | 6h | Remove sessões expiradas + audit antigo + cache de documentos |
| `AuditRetentionService` | Continuous | 24h | Drop de partições expiradas + cleanup de órfãos |
| `CrossNodeCoordinator` | Continuous | LISTEN | LISTEN pg_notify cross-pod |
| `HitlRecoveryService` | Continuous | 30s | Recovery de workflows pausados |
| `NodePersistenceService` | Continuous | Channel | Persiste node execution records em batch |
| `TokenUsagePersistenceService` | Continuous | Channel | Persiste token usage em batch (channel bounded) |
| `ToolInvocationPersistenceService` | Continuous | Channel | Persiste tool invocations em batch |
| `LlmCostRefreshService` | Continuous | 5min | Refresh de matviews com advisory lock |
| `AgUiTokenChannelCleanupService` | Continuous | 5min | Remove canais SSE órfãos |

### Modelo de Execução

Todas as fontes de execução (Api, Chat, Webhook, A2A) usam `Task.Run()` diretamente com `IExecutionSlotRegistry` para back-pressure. Nao ha mais filas ou dispatchers dedicados.

### Decisão de Design — Fire-and-Forget com Try/Catch

O projeto usa `_ = Task.Run(async () => ...)` em locais para operações que não devem bloquear o path principal (persistência HITL, token count update, cross-node publish, etc.). **Todas as instâncias seguem o padrão:**

```csharp
_ = Task.Run(async () =>
{
    try { await _repo.UpdateAsync(request); }
    catch (Exception ex)
    {
        _logger.LogError(ex, "[Context] Falha ao persistir ...");
    }
});
```

**Convenção do projeto:**
- Fire-and-forget **sempre** deve ter try/catch interno com logging (LogError ou LogWarning)
- Usado para operações **best-effort** que não afetam o resultado da execução principal
- Exemplos: `HumanInteractionService` (3×), `TokenCountUpdater` (1×), `PgEventBus` WaitAsync loop (1×), `AgUiDisconnectRegistry` (1×), `CrossNodeCoordinator` (1×)
- Operações críticas (budget, slot registry) **nunca** usam fire-and-forget

---

## 19. Database Schema

### Tabelas (19)

```
src/EfsAiHub.Infra.Persistence/DbContext/AgentFwDbContext.cs
```

| Tabela | PK | Notas |
|--------|----|-------|
| `projects` | Id | Settings/LlmConfig/Budget como JSONB |
| `agent_definitions` | Id | Data como text (JSON), ProjectId, query filter |
| `agent_versions` | AgentVersionId | Append-only, ContentHash unique |
| `agent_prompt_versions` | RowId | (AgentId, VersionId) unique |
| `skills` | Id | Data como JSONB, ContentHash, ProjectId |
| `skill_versions` | SkillVersionId | (SkillId, Revision) unique |
| `workflow_definitions` | Id | Data como text, ProjectId, Visibility |
| `workflow_versions` | WorkflowVersionId | (DefinitionId, Revision) unique, ContentHash |
| `workflow_executions` | ExecutionId | Status, composite index (WorkflowId, Status, StartedAt DESC) |
| `workflow_event_audit` | Id (auto) | Particionada mensalmente |
| `workflow_checkpoints` | ExecutionId | Data como bytea |
| `node_executions` | RowId | (ExecutionId, NodeId) unique |
| `conversations` | ConversationId | ProjectId, query filter |
| `chat_messages` | MessageId | ConversationId, StructuredOutput JSONB |
| `human_interactions` | InteractionId | Status, ExecutionId |
| `agent_sessions` | SessionId | ExpiresAt index |
| `llm_token_usage` | Id (auto) | Particionada mensalmente; coluna `CachedTokens` (F1) captura prompt cache hits |
| `persona_prompt_templates` | Id (auto) | `UX_persona_prompt_templates_Scope` unique; `CHECK template_length` ≤ 50000 chars (F3); `ActiveVersionId UUID` aponta pra version ativa (F5) |
| `persona_prompt_template_versions` | Id (auto) | F5 — append-only; FK `TemplateId` com ON DELETE CASCADE; UNIQUE(`VersionId`); índice `(TemplateId, CreatedAt DESC)` |
| `tool_invocations` | Id (auto) | Particionada mensalmente |
| `model_pricing` | Id (auto) | numeric(20,10) para precisão de preço |
| `background_response_jobs` | JobId | IdempotencyKey partial unique index |
| `ativos` | Ticker | Dados de mercado |
| `document_extraction_cache` | (sha256, model, features_hash) | expires_at TTL |

### Query Filters (Multi-Tenancy)

EF Core `HasQueryFilter` em ProjectId para: Workflows, Agents, Skills, Conversations.

### Índices Compostos

```
src/EfsAiHub.Infra.Persistence/Migrations/20260407164739_AddCompositeIndexes.cs
```

- `(workflow_id, status, started_at DESC)` em workflow_executions
- `(agent_id, version_id)` unique em agent_prompt_versions
- `(execution_id, node_id)` unique em node_executions
- `(execution_id, id)` em workflow_event_audit
- `(status, created_at)` em background_response_jobs
- Partial unique em `idempotency_key WHERE idempotency_key IS NOT NULL`

---

## 20. Pools de Conexão PostgreSQL

```
src/EfsAiHub.Host.Api/Program.cs
```

| Pool | Propósito | Min | Max (default) | Config |
|------|-----------|-----|---------------|--------|
| `general` | Chat path + escritas | 10 | 200 | `Npgsql:GeneralMinPoolSize/MaxPoolSize` |
| `sse` | LISTEN long-lived (dispatcher + CrossNode) | — | 10 | `Npgsql:SseMaxPoolSize` |
| `reporting` | Analytics read-only | — | 20 | `Npgsql:ReportingMaxPoolSize` |

Soma dos pools = **230 slots**. Postgres sobe com `max_connections=300` (ver `docker-compose.yml`), deixando ~70 slots de folga para outros consumidores (superuser, admin queries, etc.).

O pool `sse` ficou muito pequeno após a introdução do [PgNotifyDispatcher](#201-arquitetura-pub-sub-e-limites-operacionais) — 1 conn pra ele + 2 pra CrossNodeCoordinator + folga. Antes consumia 1 conn por subscriber SSE (teto ~50 clientes concorrentes).

O pool `reporting` pode apontar para read replica via `ConnectionStrings:PostgresReporting` (fallback para main se não configurado).

Registrados via `AddKeyedSingleton<NpgsqlDataSource>("pool-name", ...)`.

---

## 20.1. Arquitetura pub/sub e limites operacionais

### Padrão atual: dispatcher singleton com fanout in-memory

`src/EfsAiHub.Infra.Messaging/PgNotifyDispatcher.cs` é um `IHostedService` singleton que:

1. Abre **uma única conexão PG persistente** do pool `sse` no startup
2. Executa `LISTEN wf_events` (canal global, um para todo o processo)
3. Mantém `ConcurrentDictionary<executionId, List<ChannelWriter>>` em memória
4. Ao receber NOTIFY, desserializa, pega `ExecutionId` do payload e escreve nos channels dos subscribers registrados daquela execução

`PgEventBus.SubscribeAsync` (o consumidor) não abre conn própria — registra um `Subscription` no dispatcher e drena o `ChannelReader`. Replay do histórico via `workflow_event_audit` + dedup por `SequenceId` é preservado, eliminando race entre replay e primeiros NOTIFYs.

Reconexão automática com backoff exponencial (200ms → 10s) se a conn do dispatcher cair. Durante a janela de reconexão, novos NOTIFY podem ser perdidos, mas o replay do histórico no próximo `SubscribeAsync` cobre.

### Capacidade — teto atual: ~1000+ SSE concorrentes por instância

Capacidade é limitada por memória do processo (ordem de milhares de ChannelReader), não mais pelo pool SSE. **Validado em load test** com 100 VUs × 2 turnos passando 100/100 em ambos os turnos (`scripts/load/atendimento_cliente_burst.js VUS=100`).

Sinais para monitorar:

- Métrica `eventbus.active_subscriptions` — agora proxy direto de clientes SSE conectados, não do pool
- Métrica `eventbus.background_task.timeouts` — sinaliza instabilidade no WaitAsync do dispatcher (conn caindo). Spikes >0 indicam problema de rede com o PG
- Log `[PgNotifyDispatcher] Conexão LISTEN caiu. Reconectando…` — backoff em ação

### Bug historicamente observado (arquitetura anterior)

Sob burst de 30 conexões SSE × 2 turnos, ~40% dos segundos turnos falhavam com `Npgsql.NpgsqlOperationInProgressException: The connection is already in state 'Waiting'`. A arquitetura de "1 conn dedicada por subscriber" sofria de race entre `linkedCts.Cancel()` e `conn.DisposeAsync()`. O fix da Fase 1 (await da task background antes do dispose) resolveu o sintoma, mas o teto estrutural em ~50 subscribers permanecia. **Fase 3 eliminou o gargalo estrutural** migrando para o dispatcher descrito acima.

### Regressão

Oráculo: `scripts/load/atendimento_cliente_burst.js` (k6). Rodar com `VUS=100 ./scripts/load/run_burst.sh`. Threshold `turn{N}_success >= VUS-1` em cada turno. Baselines em `scripts/load/baselines/` com snapshot de cada marco.

Testes de integração:
- `PgEventBusLifecycleTests` — subscribe/dispose/resubscribe, concorrência entre executions, cancel precoce
- `PgNotifyDispatcherTests` — routing por executionId, fanout de subscribers da mesma execution, subscribers descartados, stress de 30 execuções concorrentes sem cross-talk

### Observabilidade

OTel disponível em `EfsAiHub.Api.EventBus` (ActivitySource). Spans:
- `eventbus.subscribe` (raiz)
- `eventbus.subscribe.open_conn`, `eventbus.subscribe.listen`, `eventbus.subscribe.replay`, `eventbus.subscribe.dispose`

Métricas (meter `EfsAiHub.Api`):
- `eventbus.active_subscriptions` — UpDownCounter, proxy direto do uso do pool SSE
- `eventbus.background_task.timeouts` — Counter, sinaliza pressão sobre pool (conn potencialmente devolvida em estado inconsistente)
- `eventbus.subscribe.setup_errors` — Counter com tag `phase` (open|listen|replay), útil para separar falha de infra vs. falha da execution

### Regressão

Oráculo reproduzível em `scripts/load/atendimento_cliente_burst.js` (k6). Rodar com `./scripts/load/run_burst.sh`. Threshold `turn2_success >= 29` em 30 VUs × 2 turnos. Baseline pós-fix disponível em `scripts/load/baselines/burst-2turns-postfix.html`. Se o teste regredir, o primeiro sinal é `NpgsqlOperationInProgressException` no log do backend.

Testes de integração em `tests/EfsAiHub.Tests.Integration/Messaging/PgEventBusLifecycleTests.cs` cobrem resubscribe em rajada, cancel precoce e subscribers concorrentes.

---

## 20.2. Pricing Document Intelligence

Provider **Azure AI Document Intelligence** cobra **por página processada** (não por token). Tabela dedicada `aihub.document_intelligence_pricing` mantém a tarifa vigente:

| Modelo | Preço por 1.000 páginas (USD) | Uso |
|---|---|---|
| `prebuilt-layout` | $10.00 | Padrão do executor `document_intelligence` — entrega markdown com tabelas/headers |
| `prebuilt-read` | $1.50 | OCR puro (mais barato, sem estrutura) |
| `prebuilt-invoice` | $10.00 | Notas fiscais |
| `prebuilt-receipt` | $10.00 | Recibos |
| `prebuilt-idDocument` | $10.00 | Documentos de identidade |

Valores em [db/seed_document_intelligence_pricing.sql](../db/seed_document_intelligence_pricing.sql). Azure pay-as-you-go tem paridade com OpenAI direct (confirmado em Microsoft Learn Q&A).

**Atualizar preço em produção**: inserir nova linha com `EffectiveFrom` posterior — o runtime lê a entrada vigente mais recente. O cache in-memory (+Redis, TTL 5min) é invalidado automaticamente pelo `DocumentIntelligenceAdminController` em upserts.

**Add-ons não incluídos** (decisão consciente): high-res OCR (+$6/1k), query fields (+$10/1k). Se algum workflow precisar, modelar como features separadas (backlog DI-1).

**Fonte em runtime**: `DocumentIntelligenceFunctions.ResolveCostAsync` consulta `IDocumentIntelligencePricingCache` (→ Redis → PG). Fallback hardcoded com os mesmos valores do seed, pra extração não falhar em ambiente sem seed aplicado.

**Visualização**: `/api/admin/document-intelligence/{usage,jobs,pricing}` e tela `/costs/document-intelligence` (+ `/costs/document-intelligence/pricing` para CRUD admin).

---

## 21. Redis (Padrões de Uso)

### Prefixo Global

Todas as chaves prefixadas com `Redis:KeyPrefix` (default: `"efs-ai-hub:"`) via `IEfsRedisCache`.

### Mapa de Chaves

| Categoria | Padrão | TTL | Uso |
|-----------|--------|-----|-----|
| **Slots** | `slots:{scope}` | Dinâmico | Concorrência distribuída (Lua INCR/DECR) |
| **Agent Def Cache** | `agent-def:{agentId}` | 5 min | Cache de definição |
| **Workflow Def Cache** | `workflow-def:{workflowId}` | 5 min | Cache de definição |
| **Prompt Cache** | `agent-prompt:active:{agentId}` | 5 min | Versão ativa do prompt |
| **Model Pricing** | `pricing:{modelId}` | 5 min | Cache de preço |
| **Budget Tokens** | `budget:tokens:{projectId}:{date}` | 48h | Contador diário de tokens |
| **Budget Custo** | `budget:cost:{projectId}:{date}` | 48h | Contador diário de custo |
| **Rate Limit Chat** | `rl:chat:{userId}` | Window | Sliding window per-user |
| **Rate Limit Conv** | `rl:chat:conv:{conversationId}` | Window | Sliding window per-conversa |
| **Rate Limit Project** | `rl:project:{projectId}` | Window | Sliding window per-projeto |
| **AG-UI State** | `agui:state:{threadId}` | 2h | Shared state cross-pod |
| **Doc Intelligence** | `{docRef}:full` | 7d | Cache de extração com gzip |
| **Data Protection** | `DataProtection:Keys` | 730d | Key ring de criptografia |

---

## 22. Observabilidade (Métricas e Tracing)

### Activity Sources (Tracing)

```
src/EfsAiHub.Infra.Observability/Tracing/ActivitySources.cs
```

| Source | Nome | Uso |
|--------|------|-----|
| `WorkflowExecutionSource` | `EfsAiHub.Api.Execution` | Span de workflow |
| `AgentInvocationSource` | `EfsAiHub.Api.AgentInvocation` | Span por agente |
| `LlmCallSource` | `EfsAiHub.Api.LlmCall` | Span por chamada LLM |
| `ToolCallSource` | `EfsAiHub.Api.ToolCall` | Span por invocação de tool |

### MetricsRegistry (40+ instrumentos)

```
src/EfsAiHub.Infra.Observability/Metrics/MetricsRegistry.cs
```

**Meter:** `"EfsAiHub.Api"` v1.0.0

**Counters:**
- `workflows.triggered/completed/failed/cancelled`
  - `workflows.failed` tem tag `error.category` (Timeout | BudgetExceeded | HitlRejected | CheckpointRecoveryFailed | FrameworkError | AgentError | ToolError | InvalidConfig | DependencyFailure | CircuitOpen | Cancelled | Unknown) — permite quebrar no Grafana/OTel por tipo de falha e priorizar. Espelhada pelo endpoint `GET /api/analytics/executions/failure-breakdown` para consumo no frontend (card "Falhas por Categoria" em `/metrics`).
- `llm.retries`, `llm.budget.exceeded`
- `llm.circuit_breaker.opened/rejected/fallbacks`
- `hitl.requested/resolved/orphaned_recoveries/recoveries`
- `crossnode.cancel.received/hitl_resolved.received`
- `tool.account.overrides/rejections/output_anomaly`
- `tool.invocations.by_fingerprint` (tags: tool, fingerprint)
- `agent.escalation.signals` (tags: category, routed)
- `chat.backpressure.rejections`, `chat.stale_completion.skipped`
- `persistence.channel.dropped` (tags: channel)

**Histograms:**
- `workflows.duration_ms`, `agents.tokens_used`, `agents.cost_usd`
- `agent.invocation.duration`
- `agent.version.resolve_latency`
- `rag.retrieval.latency`, `rag.docs.returned`
- `hitl.resolution_duration_seconds`

**Gauges:**
- `workflows.active_executions`, `chat.active_executions`
- `hitl.recovery.backlog`, `hitl.pending_age_seconds`

### TokenBatcher

```
src/EfsAiHub.Infra.Observability/Services/TokenBatcher.cs
```

Reduz NOTIFY calls de ~2000/execução para ~30-50 agrupando tokens em buffer com flush a cada 75ms. Detecta troca de agente para flush imediato.

### OpenTelemetry Export

Configurado via `OpenTelemetry:OtlpEndpoint` — suporta Jaeger, Tempo, etc.

---

## 23. Configuração Global

### Options Classes

| Classe | Seção appsettings | Propriedades Chave |
|--------|-------------------|-------------------|
| `WorkflowEngineOptions` | `WorkflowEngine` | MaxConcurrentExecutions, ChatMaxConcurrentExecutions, CheckpointMode, HITL recovery, retention days, DisconnectGracePeriodSeconds |
| `CircuitBreakerOptions` | `CircuitBreaker` | FailureThreshold (5), OpenDurationSeconds (30), HalfOpenTimeoutSeconds (10), EffectiveReplicaCount (1) — divide o threshold proporcionalmente em deploys multi-pod |
| `ChatRateLimitOptions` | `ChatRateLimit` | MaxMessages (10), WindowSeconds (60), per-conversation limits |
| `ChatRoutingOptions` | `ChatRouting` | DefaultWorkflows (map userType → workflowId) |
| `DocumentIntelligenceOptions` | `DocumentIntelligence` | Endpoint, ApiKey, UseManagedIdentity, MaxFileSizeBytes, timeouts, CacheTtlDays |
| `AdminOptions` | `Admin` | AccountIds (lista de admins) |
| `ObservabilityOptions` | `OpenTelemetry` | ServiceName, OtlpEndpoint, EnableSensitiveData |
| `OpenAIOptions` | `OpenAI` | ApiKey, OrgId |
| `AzureAIOptions` | `Azure:AI` | Endpoint, ApiKey, DeploymentId |

### Connection Pools

```json
{
  "Npgsql": {
    "GeneralMinPoolSize": 10,
    "GeneralMaxPoolSize": 500,
    "SseMaxPoolSize": 50,
    "ReportingMaxPoolSize": 20
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "KeyPrefix": "efs-ai-hub:",
    "DefinitionCacheTtlSeconds": 300
  }
}
```

---

## 24. API de Introspecção

### FunctionsController

```
src/EfsAiHub.Host.Api/Controllers/FunctionsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/functions` | Lista todas as tools, executores, middlewares e providers registrados |

Retorna fingerprints SHA-256 de cada tool para detecção de drift.

### EnumsController

```
src/EfsAiHub.Host.Api/Controllers/EnumsController.cs
```

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/enums` | Retorna todos os enums: OrchestrationMode, EdgeTypes, WorkflowStatus, InteractionType, etc. |

**Público** (não requer admin).

### InteractionsController

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/interactions/pending` | Listar HITLs pendentes |
| GET | `/api/interactions/{id}` | Obter interação |
| POST | `/api/interactions/{id}/resolve` | Resolver com aprovação/rejeição |
| GET | `/api/interactions/by-execution/{executionId}` | Listar por execução |

---

## Referência Rápida de Arquivos

| Componente | Arquivo |
|-----------|---------|
| Project Model | `src/EfsAiHub.Core.Abstractions/Projects/Project.cs` |
| ProjectSettings | `src/EfsAiHub.Core.Abstractions/Projects/ProjectSettings.cs` |
| ProjectLlmConfig | `src/EfsAiHub.Core.Abstractions/Projects/ProjectLlmConfig.cs` |
| ModelCatalog | `src/EfsAiHub.Core.Abstractions/Projects/ModelCatalog.cs` |
| ProjectsController | `src/EfsAiHub.Host.Api/Controllers/ProjectsController.cs` |
| PgProjectRepository | `src/EfsAiHub.Infra.Persistence/Postgres/PgProjectRepository.cs` |
| TenantMiddleware | `src/EfsAiHub.Host.Api/Middleware/TenantMiddleware.cs` |
| ProjectMiddleware | `src/EfsAiHub.Host.Api/Middleware/ProjectMiddleware.cs` |
| AdminGateMiddleware | `src/EfsAiHub.Host.Api/Middleware/AdminGateMiddleware.cs` |
| DefaultProjectGuard | `src/EfsAiHub.Host.Api/Middleware/DefaultProjectGuard.cs` |
| ProjectRateLimitMiddleware | `src/EfsAiHub.Host.Api/Middleware/ProjectRateLimitMiddleware.cs` |
| UserIdentityResolver | `src/EfsAiHub.Host.Api/Middleware/Identity/UserIdentityResolver.cs` |
| ProjectBudgetGuard | `src/EfsAiHub.Platform.Runtime/Guards/ProjectBudgetGuard.cs` |
| ProjectRateLimiter | `src/EfsAiHub.Platform.Runtime/Guards/ProjectRateLimiter.cs` |
| ModelPricing | `src/EfsAiHub.Core.Abstractions/Observability/ModelPricing.cs` |
| IModelPricingRepository | `src/EfsAiHub.Core.Abstractions/Observability/IModelPricingRepository.cs` |
| PgModelPricingRepository | `src/EfsAiHub.Infra.Persistence/Postgres/PgModelPricingRepository.cs` |
| ModelPricingCache | `src/EfsAiHub.Platform.Runtime/Execution/ModelPricingCache.cs` |
| ModelPricingController | `src/EfsAiHub.Host.Api/Controllers/ModelPricingController.cs` |
| AnalyticsController | `src/EfsAiHub.Host.Api/Controllers/AnalyticsController.cs` |
| TokenUsageController | `src/EfsAiHub.Host.Api/Controllers/TokenUsageController.cs` |
| SystemController | `src/EfsAiHub.Host.Api/Controllers/SystemController.cs` |
| LlmTokenUsage | `src/EfsAiHub.Core.Abstractions/Observability/LlmTokenUsage.cs` |
| ToolInvocation | `src/EfsAiHub.Core.Abstractions/Observability/ToolInvocation.cs` |
| DocumentIntelligenceFunctions | `src/EfsAiHub.Platform.Runtime/Functions/DocumentIntelligenceFunctions.cs` |
| Trading Models | `src/EfsAiHub.Core.Agents/Trading/` |
| BoletaToolFunctions | `src/EfsAiHub.Platform.Runtime/Functions/BoletaToolFunctions.cs` |
| ConversationsController | `src/EfsAiHub.Host.Api/Controllers/ConversationsController.cs` |
| ExecutionsController | `src/EfsAiHub.Host.Api/Controllers/ExecutionsController.cs` |
| AgentSessionsController | `src/EfsAiHub.Host.Api/Controllers/AgentSessionsController.cs` |
| PgCrossNodeBus | `src/EfsAiHub.Infra.Messaging/PgCrossNodeBus.cs` |
| CrossNodeCoordinator | `src/EfsAiHub.Host.Worker/Services/CrossNodeCoordinator.cs` |
| AgentFwDbContext | `src/EfsAiHub.Infra.Persistence/DbContext/AgentFwDbContext.cs` |
| MetricsRegistry | `src/EfsAiHub.Infra.Observability/Metrics/MetricsRegistry.cs` |
| ActivitySources | `src/EfsAiHub.Infra.Observability/Tracing/ActivitySources.cs` |
| TokenBatcher | `src/EfsAiHub.Infra.Observability/Services/TokenBatcher.cs` |
| AuditRetentionService | `src/EfsAiHub.Host.Worker/Services/AuditRetentionService.cs` |
| DatabaseBootstrapService | `src/EfsAiHub.Host.Worker/Services/DatabaseBootstrapService.cs` |
| LlmCostRefreshService | `src/EfsAiHub.Host.Worker/Services/LlmCostRefreshService.cs` |
| FunctionsController | `src/EfsAiHub.Host.Api/Controllers/FunctionsController.cs` |
| EnumsController | `src/EfsAiHub.Host.Api/Controllers/EnumsController.cs` |
| InteractionsController | `src/EfsAiHub.Host.Api/Controllers/InteractionsController.cs` |
| Program.cs | `src/EfsAiHub.Host.Api/Program.cs` |

---

## Localização (F8)

Ver [ADR 007](adr/007-i18n-strategy.md) pra detalhes.

**Stack**: `react-i18next` no front, `CultureInfo.CurrentUICulture` +
`RequestLocalizationMiddleware` no back. Suportadas: `pt-BR` (default),
`en-US`.

**Resolução no backend**: `RequestLocalizationMiddleware` no pipeline
(antes de `TenantMiddleware`) detecta culture via header
`Accept-Language` ou default. `PersonaBooleanFormat.Format(bool)` no
renderer lê `CurrentUICulture` pra escolher "sim/não" vs "yes/no".

**Resolução no frontend**: `i18next-browser-languagedetector` (nav
language / cookie / localStorage), fallback `pt-BR`. Namespace
`persona` pra migração incremental — páginas não-migradas continuam
com strings hardcoded.

**Trade-offs aceitos**:
- Contexto do LLM segue o **end-user** via `Accept-Language` — admin
  pt-BR operando cliente en-US ainda gera prompt em pt-BR. Corrigir
  requer `ConversationSession.Locale` + custom provider (backlog
  `I18N-CONTEXT-AWARE`).
- Só `PersonaExperimentsPage` 100% migrada no front. Demais em
  backlog `I18N-MIGRATE`.
- Mensagens de erro dos endpoints ficam em pt-BR hardcoded — UI
  faria tradução client-side se necessário. Migração pra
  `IStringLocalizer` em backlog `I18N-BACKEND-ERRORS`.
