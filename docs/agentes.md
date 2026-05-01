# Agentes — Guia Completo

> O que é um agente, o que ele tem, como criar um agente e como ele funciona no EfsAiHub.

---

## Sumário

1. [Contrato do Agente](#1-contrato-do-agente)
2. [Propriedades](#2-propriedades)
3. [Agente em Execução](#3-agente-em-execução)
4. [Tipos de Saída](#4-tipos-de-saída)
5. [Skills (Habilidades)](#5-skills-habilidades)
6. [Segurança](#6-segurança)
7. [Tools (Ferramentas)](#7-tools-ferramentas)
8. [Sessão e Contexto](#8-sessão-e-contexto)
9. [Middleware](#9-middleware)
10. [Providers](#10-providers)
11. [Versionamento de Agente](#11-versionamento-de-agente)
12. [Versionamento de Prompt](#12-versionamento-de-prompt)
13. [Sandbox](#13-sandbox)
14. [Human-in-the-Loop (HITL)](#14-human-in-the-loop-hitl)
15. [Escalation e Roteamento](#15-escalation-e-roteamento)
16. [Observabilidade](#16-observabilidade)
17. [Enrichment e Disclaimers](#17-enrichment-e-disclaimers)
18. [Rate Limiting e Concorrência](#18-rate-limiting-e-concorrência)
19. [Background Jobs e Webhooks](#19-background-jobs-e-webhooks)
20. [Knowledge Sources (RAG)](#20-knowledge-sources-rag)
21. [Categorias de Erro](#21-categorias-de-erro)
22. [Como Criar um Agente](#22-como-criar-um-agente)
23. [Agentes Cadastrados](#23-agentes-cadastrados)

---

## 1. Contrato do Agente

O agente é a unidade atômica de inteligência no EfsAiHub. Ele encapsula: instruções (system prompt), modelo LLM, provider, tools, middlewares, skills e budget.

### Modelo de Domínio

```
src/EfsAiHub.Core.Agents/Models/AgentDefinition.cs
```

```csharp
public class AgentDefinition : IProjectScoped
{
    public string ProjectId { get; set; } = "default";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required AgentModelConfig Model { get; init; }
    public AgentProviderConfig Provider { get; init; } = new();
    public string? Instructions { get; init; }
    public List<AgentToolDefinition> Tools { get; init; } = [];
    public AgentStructuredOutputDefinition? StructuredOutput { get; init; }
    public List<AgentMiddlewareConfig> Middlewares { get; init; } = [];
    public AgentProviderConfig? FallbackProvider { get; init; }
    public ResiliencePolicy? Resilience { get; init; }
    public AgentCostBudget? CostBudget { get; init; }
    public List<SkillRef> SkillRefs { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### API Contract (REST)

| Operação | Método | Endpoint |
|----------|--------|----------|
| Criar/Atualizar | `PUT` | `/api/agents/{id}` |
| Obter | `GET` | `/api/agents/{id}` |
| Listar | `GET` | `/api/agents` |
| Deletar | `DELETE` | `/api/agents/{id}` |
| Validar | `POST` | `/api/agents/{id}/validate` |
| Rollback | `POST` | `/api/agents/{id}/rollback` |
| Sandbox | `POST` | `/api/agents/{id}/sandbox` |
| Comparar versões | `POST` | `/api/agents/{id}/compare` |
| Versões | `GET` | `/api/agents/{id}/versions` |

---

## 2. Propriedades

### AgentModelConfig

| Propriedade | Tipo | Descrição |
|-------------|------|-----------|
| `DeploymentName` | string (required) | Nome do modelo/deployment no provider |
| `Temperature` | float? | Temperatura (0.0–2.0) |
| `MaxTokens` | int? | Máximo de tokens de saída |

### AgentProviderConfig

| Propriedade | Tipo | Default | Descrição |
|-------------|------|---------|-----------|
| `Type` | string | "AzureFoundry" | `AzureFoundry` \| `AzureOpenAI` \| `OpenAI` |
| `ClientType` | string | "ChatCompletion" | `ChatCompletion` \| `Responses` \| `Assistants` |
| `Endpoint` | string? | null | URL do endpoint (usa global se omitido) |
| `ApiKey` | string? | null | API key (usa DefaultAzureCredential se omitido) |

### AgentToolDefinition

| Propriedade | Tipo | Descrição |
|-------------|------|-----------|
| `Type` | string (required) | `function` \| `mcp` \| `code_interpreter` \| `file_search` \| `web_search` |
| `Name` | string? | Nome da função (obrigatório para `function`) — convenção `snake_case` em inglês; descrição em pt-BR. Ver §7 |
| `RequiresApproval` | bool | Declarado para HITL per-tool (Phase futura — não enforced atualmente) |
| `FingerprintHash` | string? | SHA-256 do schema da tool (versionamento imutável) |
| `McpServerId` | string? | **Preferido** para `type=mcp` — aponta para registro em [`/mcp-servers`](./mcp.md). O runtime resolve `ServerLabel`/`ServerUrl`/`AllowedTools`/`Headers` via `IMcpServerRepository`. |
| `ServerLabel` | string? | *(legacy/fallback BC — preferir `McpServerId`)* Label do servidor MCP. |
| `ServerUrl` | string? | *(legacy/fallback BC — preferir `McpServerId`)* URL do servidor MCP. |
| `AllowedTools` | List\<string\> | *(legacy/fallback BC — preferir `McpServerId`)* Whitelist de tools MCP permitidas inline. |
| `RequireApproval` | string? | `"never"` \| `"always"` (apenas MCP) |
| `Headers` | Dictionary | *(legacy/fallback BC — preferir `McpServerId`)* Headers customizados inline. |
| `ConnectionId` | string? | ID de conexão Azure Foundry (para `web_search`) |

### AgentStructuredOutputDefinition

| Propriedade | Tipo | Descrição |
|-------------|------|-----------|
| `ResponseFormat` | string | `text` \| `json` \| `json_schema` |
| `SchemaName` | string? | Identificador do schema (obrigatório para `json_schema`) |
| `Schema` | JsonDocument? | JSON Schema raw (obrigatório para `json_schema`) |

### ResiliencePolicy

| Propriedade | Default | Descrição |
|-------------|---------|-----------|
| `MaxRetries` | 3 | Tentativas máximas |
| `InitialDelayMs` | 1000 | Delay inicial (ms) |
| `BackoffMultiplier` | 2.0 | Multiplicador exponencial |
| `RetriableHttpStatusCodes` | null | Status codes para retry (default: 429, 500, 502, 503) |
| `CallTimeoutMs` | null | Timeout máximo em ms por chamada LLM individual (sem acumular retries). Null/≤0 = sem timeout adicional. Recomendado em produção: 60000 para não-streaming; em streaming, aplica só à conexão inicial. |
| `JitterRatio` | 0.0 | Fração de jitter aplicada ao backoff para reduzir thundering herd (0.0 a 1.0). Ex: 0.1 adiciona até 10% de jitter aleatório sobre o delay. Recomendado em produção: 0.1. |

### AgentCostBudget

| Propriedade | Descrição |
|-------------|-----------|
| `MaxCostUsd` | Teto de custo em USD por execução |

---

## 3. Agente em Execução

### Cadeia de Chamada LLM (Pipeline)

```
RetryingChatClient           ← Retry com backoff exponencial
    ↓
CircuitBreakerChatClient     ← Failover para provider alternativo
    ↓
[Middlewares do Agente]      ← AccountGuard, StructuredOutputState, etc.
    ↓
TokenTrackingChatClient      ← Contagem de tokens + budget enforcement
    ↓
FunctionInvokingChatClient   ← Auto-invoca tools em loop
    ↓                          (explícito em Graph mode;
IChatClient (Raw Provider)     implícito via framework em AIAgent mode)
```

### Modos de Orquestração

| Modo | Comportamento |
|------|--------------|
| **Sequential** | Agentes executam em sequência, output alimenta o próximo |
| **Concurrent** | Agentes executam em paralelo, resultados agregados |
| **Handoff** | Agente pode transferir para outro (decisão do LLM) |
| **GroupChat** | Múltiplos agentes conversam entre si com moderador |
| **Graph** | DAG declarativo com edges condicionais e nodes tipados |

### Modos de Dispatch

| Source | Comportamento |
|--------|--------------|
| **Chat** | `Task.Run` direto, slot Redis (TTL 5min), retorna 202 |
| **Interactive** (API/Webhook/A2A) | Enfileirado, processado por dispatcher |
| **Background** (Cron) | Enfileirado com timeout mais longo |

### Proteções Anti-Loop

- Ping-pong detection: A → B → A 3x consecutivas → `ErrorCategory.AgentLoopLimit`
- Max invocations: Limite configurável de handoffs
- Max 10 chamadas tool por turno (`FunctionInvokingChatClient`)
- Budget enforcement: Tokens + USD checados antes de cada chamada LLM

### Checkpointing & Recovery

- Framework cria checkpoints automáticos após cada SuperStep
- `PgCheckpointStore` persiste em PostgreSQL
- `HitlRecoveryService` (poll a cada 30s) restaura execuções Paused após restart
- `InProcessExecution.ResumeStreamingAsync()` retoma do checkpoint exato
- `DatabaseBootstrapService` marca Running/Pending como Failed no startup

---

## 4. Tipos de Saída

### Eventos AG-UI (Server-Sent Events)

| Tipo | Quando | Campos principais |
|------|--------|-------------------|
| `RUN_STARTED` | Execução iniciada | `runId`, `threadId` |
| `RUN_FINISHED` | Concluída | `output` |
| `RUN_ERROR` | Falhou | `error`, `errorCode` |
| `STEP_STARTED` | Agente ativado | `stepId`, `stepName` |
| `STEP_FINISHED` | Agente completou | `stepId`, `stepName` |
| `TEXT_MESSAGE_START` | Início de texto | `messageId`, `role` |
| `TEXT_MESSAGE_CONTENT` | Token chunk | `messageId`, `Delta` |
| `TEXT_MESSAGE_END` | Fim do texto | `messageId` |
| `TOOL_CALL_START` | Tool invocada | `toolCallId`, `toolCallName` |
| `TOOL_CALL_ARGS` | Args streaming | `toolCallId`, `Delta` |
| `TOOL_CALL_END` | Tool completa | `toolCallId` |
| `TOOL_CALL_RESULT` | Resultado | `toolCallId`, `result` |
| `STATE_SNAPSHOT` | Estado completo | `Snapshot` (JSON) |
| `STATE_DELTA` | Update incremental | `Delta` (JSON Patch RFC 6902) |
| `MESSAGES_SNAPSHOT` | Resync histórico | `Messages[]` |
| `CUSTOM` | Extensibilidade | `customName`, `customValue` |

### Fluxo de Streaming

```
Agent → TokenTrackingChatClient → AgUiTokenChannel (bounded 1000)
                                         ↓
PgEventBus (persistidos) ──→ AgUiStreamMerger (bounded 500)
                                         ↓
                              AgUiSseHandler → HTTP SSE → Frontend
```

- **Tokens:** efêmeros (não persistidos em banco)
- **Eventos lifecycle/step/tool:** persistidos em `workflow_event_audit` para replay

### Predictive State

`PredictiveStateEmitter` mapeia args de tool calls para state updates antes da tool completar:

```json
{ "get_portfolio": "/portfolio/selected" }
```

Frontend atualiza UI otimisticamente via `STATE_DELTA` enquanto tool executa.

---

## 5. Skills (Habilidades)

Pacote reutilizável de **tools + instruções + políticas**:

```csharp
public class Skill : IProjectScoped
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? InstructionsAddendum { get; init; }
    public List<AgentToolDefinition> Tools { get; init; }
    public List<string> KnowledgeSourceIds { get; init; }    // RAG (Phase 4)
    public SkillPolicy? Policy { get; init; }
    public string ContentHash { get; init; }
}
```

### Fluxo de Merge

1. `AgentDefinition.SkillRefs` → `SkillResolver` (cache 2min) → `SkillMerger.ApplySkills()`
2. Concatena `InstructionsAddendum` ao prompt (separador: `"\n\n---\n\n"`)
3. Agrega tools (deduplica por `type:name`)

### API

| Operação | Endpoint |
|----------|----------|
| CRUD | `GET/PUT/DELETE /api/skills/{id}` |
| Versões | `GET /api/skills/{id}/versions` |

---

## 6. Segurança

| Camada | Mecanismo |
|--------|-----------|
| Anti-alucinação | `AccountGuardChatClient` — reescreve contas hallucinated |
| Budget tokens/USD | `TokenTrackingChatClient` — soft cap (LogCritical + métrica `llm.budget.exceeded`, **não bloqueia**) |
| Budget USD | `IModelPricingCache` — custo incremental |
| Tool guard | `TrackedAIFunction.ApplyAccountGuard` — params sensíveis reescritos |
| Circuit breaker | Failover automático para fallback provider |
| Tool fingerprint | SHA-256 imutável do schema |
| MCP whitelist | `AllowedTools` por servidor |
| Credenciais | `IDataProtector` per-project (at-rest encryption) |
| Multi-tenancy | `ProjectId` em todas as queries |
| Disclaimers | Injeção regulatória declarativa (CVM) |
| HITL | Aprovação humana bloqueante per-node |

### AccountGuard Modos

| Modo | Comportamento |
|------|--------------|
| `ClientLocked` | Params sensíveis forçados para userId. Números diferentes na resposta substituídos. |
| `AssessorLogOnly` | Divergência logada (assessores operam múltiplas contas) |
| `None` | Sem enforcement |

---

## 7. Tools (Ferramentas)

### Tipos

| Tipo | Onde roda | Descrição |
|------|-----------|-----------|
| `function` | In-process | Função C# no registry |
| `mcp` | HTTP | Servidor MCP externo — **referenciado por Id** via registry centralizado ([`/mcp-servers`](./mcp.md)). |
| `code_interpreter` | Server-side | Azure Foundry |
| `file_search` | Server-side | Azure Foundry |
| `web_search` | Server-side | Azure Foundry |

### Convenção de nomenclatura

Nomes de tools e code executors devem ser em **inglês (`snake_case`)** — consistente
com o padrão da OpenAI/Azure e com a expectativa do LLM (ele pensa em inglês antes de
traduzir para o idioma do usuário). As **descrições** (via atributo `[Description]`)
permanecem em **português**, que é o idioma dos prompts e dos usuários da plataforma.

Function tools atuais: `search_asset`, `get_asset_position`, `get_portfolio`,
`redeem_asset`, `invest_asset`, `calculate_asset_redemption_tax`, `SendOrder`,
`search_web`, `confirm_boleta`, etc.  Code executors: `service_pre_processor`,
`service_post_processor`, `document_intelligence`, `pix_validate`, etc.

Lista completa em `/tools` na UI (com "Usado por N agents" em cada linha).

### MCP Servers Registry

Agents referenciam MCPs pelo `McpServerId` ao invés de duplicar inline URL, label e allowed tools em cada definição. Ver [`docs/mcp.md`](./mcp.md) para o contrato completo, CRUD via `/api/admin/mcp-servers`, UI em `/mcp-servers` e resolução live pelo `AzureFoundryClientProvider`.

Não há validação de saúde (health check) no create/update do agent — cadastre MCPs mesmo que o endpoint esteja offline temporariamente.

### Implementação e Registro

```csharp
public class MinhasFunctions
{
    [Description("Busca cotação de um ativo")]
    public async Task<string> BuscarCotacao(
        [Description("Ticker (ex: PETR4)")] string ticker)
    {
        return JsonSerializer.Serialize(await _service.GetQuoteAsync(ticker));
    }
}

// Program.cs:
registry.Register("buscar_cotacao", AIFunctionFactory.Create(funcs.BuscarCotacao));
registry.Register("custom_tool", AIFunctionFactory.Create(fn), projectId: "mesa-rv"); // project-scoped
```

### ICodeExecutorRegistry (Graph mode)

Para nós de código puro (sem LLM):
```csharp
registry.Register<PortfolioInput, PortfolioOutput>("calculate_risk", myHandler);
```

### Visibilidade

| Escopo | Resolução |
|--------|-----------|
| Global | Sempre disponível |
| Project | Project first → Global fallback |

---

## 8. Sessão e Contexto

### Sessions (Standalone Multi-Turn)

```csharp
public class AgentSessionRecord
{
    public required string SessionId { get; init; }
    public required string AgentId { get; init; }
    public JsonElement SerializedState { get; set; }  // Opaco (framework)
    public int TurnCount { get; set; }
}
```

- TTL: 30 dias (renovável a cada turno)
- Cleanup: `AgentSessionCleanupService` a cada 6h
- Estado opaco gerenciado pelo Microsoft.Agents.AI

### API de Sessions

| Endpoint | Descrição |
|----------|-----------|
| `POST /api/agents/{id}/sessions` | Criar sessão |
| `POST /api/agents/{id}/sessions/{sid}/run` | Turno (blocking) |
| `POST /api/agents/{id}/sessions/{sid}/stream` | Turno (SSE) |
| `GET/DELETE /api/agents/{id}/sessions/{sid}` | Get/Delete |

### Contexto em Workflows (ChatTurnContext)

Limites upstream (ConversationService):
- `MaxHistoryMessages`: default 20 (hard cap)
- `MaxHistoryTokens`: opcional (trim por budget)
- `contextClearedAt`: "limpa conversa"

### Shared State (AG-UI)

| Camada | TTL | Propósito |
|--------|-----|-----------|
| L1: MemoryCache | 30min sliding | Fast path in-pod |
| L2: Redis | 2h fixed | Cross-pod, reconnect |

Hard cap: 32KB por thread. Write-through L1→L2.

---

## 9. Middleware

O pipeline LLM do agente é composto por **6 DelegatingChatClients** encadeados. Dois são configuráveis via `AgentDefinition.Middlewares` (registry) e quatro são fixos na cadeia.

### Pipeline Completo (de fora para dentro)

```
Request do Workflow
    ↓
┌─────────────────────────────────────────────────────────────┐
│ 1. RetryingChatClient              [FIXO]                   │
│    Retry com backoff exponencial em falhas transientes       │
├─────────────────────────────────────────────────────────────┤
│ 2. CircuitBreakerChatClient        [FIXO]                   │
│    Failover automático para provider alternativo             │
├─────────────────────────────────────────────────────────────┤
│ 3. [Middlewares do AgentDefinition] [CONFIGURÁVEL]          │
│    AccountGuard, StructuredOutputState, custom...            │
├─────────────────────────────────────────────────────────────┤
│ 4. TokenTrackingChatClient         [FIXO]                   │
│    Contagem de tokens, budget enforcement, métricas          │
├─────────────────────────────────────────────────────────────┤
│ 5. FunctionInvokingChatClient      [FIXO]                   │
│    Auto-invoca tools quando LLM solicita (loop)              │
│    (explícito em Graph mode; implícito em AIAgent mode)      │
├─────────────────────────────────────────────────────────────┤
│ 6. IChatClient (Raw Provider)                                │
│    Chamada real ao LLM (Azure OpenAI / OpenAI / Foundry)     │
└─────────────────────────────────────────────────────────────┘
```

---

### 9.1 RetryingChatClient (Fixo — Outermost)

**Arquivo:** `src/EfsAiHub.Platform.Runtime/Factories/RetryingChatClient.cs`

**O que faz:** Intercepta falhas transientes do LLM e repete a chamada com backoff exponencial.

**Comportamento:**

| Aspecto | Detalhe |
|---------|---------|
| Retry trigger | HTTP 429 (TooManyRequests), 500, 502, 503 + **timeout per-call** (se configurado) |
| Custom codes | Configurável via `ResiliencePolicy.RetriableHttpStatusCodes` |
| Max retries | Default 3 (configurável por agente) |
| Backoff | `initialDelay × backoffMultiplier^attempt` (1s → 2s → 4s), com **jitter opcional** (`JitterRatio`) |
| Timeout per-call | `ResiliencePolicy.CallTimeoutMs` cobre cada tentativa individualmente (não acumula). Em streaming, aplica só à conexão inicial. |
| Cancel do usuário | `CancellationToken` externo sempre propaga sem retry (cancelamento do cliente ≠ timeout nosso). |
| Streaming | Retry apenas na **conexão inicial** (antes do primeiro chunk). Uma vez streaming, erros propagam imediatamente. |
| Métricas | Counter `LlmRetries` com tags: `agent.id`, `model.id`, `attempt`, `status_code`, `timeout_triggered` |

**Exemplo de configuração no agente:**
```json
{
  "resilience": {
    "maxRetries": 5,
    "initialDelayMs": 2000,
    "backoffMultiplier": 1.5,
    "callTimeoutMs": 60000,
    "jitterRatio": 0.1,
    "retriableHttpStatusCodes": [429, 500, 502, 503, 504]
  }
}
```

**Distinção timeout vs cancel:** um provider pendurado dispara o `CallTimeoutMs` (tag `timeout_triggered=true` na métrica, retenta). Um `CancellationToken` cancelado externamente (usuário saiu da tela, workflow timeout) propaga `OperationCanceledException` imediatamente sem retry.

---

### 9.2 CircuitBreakerChatClient (Fixo)

**Arquivo:** `src/EfsAiHub.Platform.Runtime/Resilience/CircuitBreakerChatClient.cs`

**O que faz:** Monitora falhas consecutivas do provider e automaticamente redireciona para o fallback quando o circuit abre.

**Máquina de estados:**

```
     sucesso          5 falhas consecutivas
 ┌──────────┐      ┌──────────────────────┐
 │          │      │                      │
 │  CLOSED  │─────►│        OPEN          │
 │ (normal) │      │ (bloqueia requests)  │
 │          │◄─────│                      │
 └──────────┘      └──────────┬───────────┘
   ▲ probe ok                 │ após 30s
   │                          ▼
   │              ┌───────────────────────┐
   └──────────────│      HALF-OPEN        │
     probe ok     │ (permite 1 request)   │
                  └───────────────────────┘
                    probe falha → volta OPEN
```

| Config | Default | Descrição |
|--------|---------|-----------|
| `FailureThreshold` | 5 | Falhas consecutivas para abrir |
| `OpenDurationSeconds` | 30 | Tempo no estado Open |
| `HalfOpenTimeoutSeconds` | 10 | Timeout do probe |
| `Enabled` | true | Pode desabilitar globalmente |

**Fallback routing:**
- Quando Open: redireciona para `FallbackProvider` (se configurado e Type diferente)
- Sem fallback: throw `CircuitOpenException`
- Singleton por provider key: `"{ProviderType}:{Endpoint}"`

**Detecção de falha transiente:** HTTP 429, 500, 502, 503

**Métricas:** `CircuitBreakerOpened`, `CircuitBreakerFallbacks`, `CircuitBreakerRejected`

---

### 9.3 AccountGuardChatClient (Configurável — Registry)

**Arquivo:** `src/EfsAiHub.Platform.Runtime/Guards/AccountGuardChatClient.cs`
**Tipo no registry:** `"AccountGuard"` | **Phase:** `Both` (antes e depois do LLM)

**O que faz:** Protege contra o LLM inventar ou trocar números de conta financeira na resposta. Essencial para compliance no mercado financeiro.

**Fluxo OnBefore (antes do LLM):**
1. Extrai conta original de 3 fontes (prioridade):
   - `ChatTurnContext.UserId` (JSON do input)
   - System message regex: `conta: XXXXX` ou `account: XXXXX`
   - Settings `fixedAccount` (override manual)
2. Armazena conta para comparação pós-LLM

**Fluxo OnAfter (depois do LLM):**
1. Aplica regex na resposta: `(?<=conta[:\s]+|account[:\s]+|operacional[:\s]+)\d{5,10}`
2. Para cada número encontrado diferente do original:
   - **Heurística:** Só substitui se comprimento ±1 dígito do original (evita destruir preços como "15000")
   - **ClientLocked:** Substitui pelo número correto
   - **AssessorLogOnly:** Apenas loga divergência
3. Suporta streaming: acumula buffer de texto e aplica regex em cada chunk

**Configuração:**
```json
{
  "type": "AccountGuard",
  "enabled": true,
  "settings": {
    "mode": "ClientLocked",
    "fixedAccount": "12345",
    "accountPattern": "\\d{5,8}"
  }
}
```

| Setting | Tipo | Descrição |
|---------|------|-----------|
| `mode` | string | `ClientLocked` \| `AssessorLogOnly` (obrigatório) |
| `fixedAccount` | string? | Conta fixa (override de todas as fontes) |
| `accountPattern` | regex? | Pattern customizado de detecção |

---

### 9.4 StructuredOutputStateChatClient (Configurável — Registry)

**Arquivo:** `src/EfsAiHub.Platform.Runtime/Guards/StructuredOutputStateChatClient.cs`
**Tipo no registry:** `"StructuredOutputState"` | **Phase:** `Post` (apenas depois do LLM)

**O que faz:** Intercepta respostas JSON válidas do agente e automaticamente atualiza o AG-UI shared state para o frontend. Zero overhead de tokens (não modifica prompt nem adiciona instruções).

**Fluxo OnAfter:**
1. Verifica se resposta é JSON válido (object ou array)
2. Se válido: chama `ExecutionContext.UpdateSharedState(path, value)`
3. Path template: `agents/{stateKey}` (default: agentId)
4. Emite `STATE_DELTA` via SSE para o frontend
5. Se inválido: passa resposta sem modificação

**Casos de uso:**
- Agente com `ResponseFormat = "json"` retorna dados estruturados
- Frontend mostra resultado em tempo real via shared state
- Não precisa de tool call — o próprio response do agente atualiza o state

**Configuração:**
```json
{
  "type": "StructuredOutputState",
  "enabled": true,
  "settings": {
    "stateKey": "analise-resultado"
  }
}
```

| Setting | Tipo | Default | Descrição |
|---------|------|---------|-----------|
| `stateKey` | string? | agentId | Campo no shared state onde o JSON é escrito |

**Non-streaming:** Intercepta `ChatResponse.Messages` completo.
**Streaming:** Acumula texto até fechar JSON, então emite state update.

---

### 9.5 TokenTrackingChatClient (Fixo)

**Arquivo:** `src/EfsAiHub.Platform.Runtime/Factories/TokenTrackingChatClient.cs`

**O que faz:** Contabiliza tokens consumidos (input + output), calcula custo em USD, enforce budget limits, emite métricas e persiste audit trail.

**Fluxo a cada chamada LLM:**

```
1. ANTES: Observa budget (soft cap — não bloqueia execução)
   ├─ TotalTokens ≥ MaxTokensPerExecution?     → LogCritical + counter (uma vez por execução)
   ├─ TotalCostUsd ≥ MaxCostUsd?                → LogCritical + counter (uma vez por execução)
   └─ TotalCostUsd ≥ AgentMaxCostUsd?           → LogCritical + counter (uma vez por execução)

2. DURANTE: Chama inner client (LLM real)

3. DEPOIS: Registra consumo
   ├─ Extrai UsageDetails (input/output tokens) do response
   ├─ Incrementa ExecutionBudget (thread-safe: Interlocked + lock)
   ├─ Calcula custo: (inputTokens × pricePerInput) + (outputTokens × pricePerOutput)
   ├─ Enqueue LlmTokenUsage no Channel (background persistence)
   ├─ Emite OTel span "LLMCall" com tags (model, tokens, cost, duration)
   └─ Emite TOOL_CALL_ARGS via IAgUiTokenSink (para streaming de tool args)
```

**Thread-safety:**
- Tokens: `Interlocked.Add()` (lock-free)
- Custo decimal: dedicated lock (Interlocked não suporta decimal)

**Persistência:** Background worker (`TokenUsagePersistenceService`) consome Channel em batches.

**Campos persistidos em LlmTokenUsage:**
- AgentId, ModelId, ExecutionId, WorkflowId
- InputTokens, OutputTokens, TotalTokens, DurationMs
- PromptVersionId, AgentVersionId (audit trail)
- RetryCount, CreatedAt

---

### 9.6 FunctionInvokingChatClient (Fixo — Innermost)

**Arquivo:** Microsoft.Extensions.AI (library, não código do projeto)

**O que faz:** Intercepta respostas do LLM que contêm `FunctionCallContent` e automaticamente invoca as tools, retorna o resultado ao LLM, e repete até obter resposta textual final.

**Loop de auto-invocação:**

```
1. Envia messages ao LLM
       ↓
2. Response contém FunctionCallContent?
   ├─ NÃO → retorna resposta final (texto)
   └─ SIM → para cada tool call:
       ├─ Extrai tool name + args
       ├─ Resolve AIFunction no ChatOptions.Tools
       ├─ Invoca via TrackedAIFunction.InvokeCoreAsync()
       │  ├─ ApplyAccountGuard() (rewrite params sensíveis)
       │  ├─ Executa função C# real
       │  ├─ Captura resultado ou erro amigável
       │  └─ Persiste ToolInvocation (background)
       ├─ Adiciona resultado como message role=tool
       └─ Volta ao passo 1 (chama LLM de novo com contexto expandido)

3. Limite: MaximumIterationsPerRequest = 10
   Se excedido → retorna último response mesmo sem texto final
```

**Nota sobre Graph vs AIAgent mode:**
- **Graph mode:** `FunctionInvokingChatClient` é instanciado explicitamente no `AgentFactory.CreateLlmHandlerAsync()`
- **AIAgent mode:** O loop de tools é gerenciado internamente pelo `Microsoft.Agents.AI` framework quando `.AsAIAgent(options)` é chamado. O `FunctionInvokingChatClient` não aparece na cadeia explícita.

---

### Resumo Visual

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│                              PIPELINE COMPLETO                                    │
│                                                                                   │
│  RetryingChatClient ──► CircuitBreaker ──► [Middlewares] ──► TokenTracking ──► LLM │
│  ┌─────────────────┐   ┌──────────────┐  ┌─────────────┐  ┌──────────────┐       │
│  │ • Retry 429/5xx │   │ • 5 falhas   │  │ • Account   │  │ • Budget     │       │
│  │ • Backoff exp   │   │   → Open     │  │   Guard     │  │   enforce    │       │
│  │ • Max 3 (cfg)   │   │ • Fallback   │  │ • Structured│  │ • Token cnt  │       │
│  │ • Stream-aware  │   │   provider   │  │   Output    │  │ • Cost USD   │       │
│  └─────────────────┘   │ • HalfOpen   │  │   State     │  │ • OTel span  │       │
│                        │   probe      │  │ • [Custom]  │  │ • Persist    │       │
│                        └──────────────┘  └─────────────┘  └──────────────┘       │
└──────────────────────────────────────────────────────────────────────────────────┘
```

---

### Como Criar um Middleware Customizado

**Base class:**
```csharp
public abstract class AgentMiddlewareBase : DelegatingChatClient
{
    protected string AgentId { get; }
    protected IReadOnlyDictionary<string, string> Settings { get; }
    protected ILogger Logger { get; }

    protected virtual Task OnBeforeRequestAsync(IList<ChatMessage> messages, ChatOptions options);
    protected virtual Task OnAfterResponseAsync(ChatResponse response);
}
```

**Implementação:**
```csharp
public class MeuMiddleware : AgentMiddlewareBase
{
    public MeuMiddleware(IChatClient inner, string agentId,
                         Dictionary<string, string> settings, ILogger logger)
        : base(inner, agentId, settings, logger) { }

    protected override Task OnBeforeRequestAsync(IList<ChatMessage> messages, ChatOptions options)
    {
        // Modificar messages ou options antes do LLM
        Logger.LogDebug("Before LLM call for agent {AgentId}", AgentId);
        return Task.CompletedTask;
    }

    protected override Task OnAfterResponseAsync(ChatResponse response)
    {
        // Processar ou modificar resposta após LLM
        return Task.CompletedTask;
    }
}
```

**Registro:**
```csharp
middlewareRegistry.Register(
    type: "MeuMiddleware",
    phase: MiddlewarePhase.Both,
    factory: (inner, agentId, settings, logger) => new MeuMiddleware(inner, agentId, settings, logger),
    label: "Meu Middleware",
    description: "Descrição do que faz",
    settings: [new MiddlewareSettingDef("chave", "Descrição da setting", "valor_default")]
);
```

**Uso no agente:**
```json
{
  "middlewares": [
    { "type": "MeuMiddleware", "enabled": true, "settings": { "chave": "valor" } }
  ]
}
```

---

## 10. Providers

### Disponíveis

| Provider | Auth | ClientTypes |
|----------|------|-------------|
| `AzureOpenAI` | API Key / DefaultAzureCredential | ChatCompletion, Responses |
| `OpenAI` | API Key | ChatCompletion, Responses |
| `AzureFoundry` | DefaultAzureCredential | Assistants (server-side) |

### Resolução de Credenciais (Prioridade)

```
1. Project credentials (criptografadas no banco)
2. Agent definition (Provider.ApiKey)
3. Global config (appsettings.json)
4. Managed Identity (DefaultAzureCredential)
```

### Fallback (Circuit Breaker)

```json
{
  "provider": { "type": "AzureOpenAI" },
  "fallbackProvider": { "type": "OpenAI" }
}
```

5 falhas → Open (30s) → HalfOpen (1 probe) → Closed. Fallback deve ter Type diferente.

---

## 11. Versionamento de Agente

### Modelo (Append-Only)

```csharp
public sealed record AgentVersion
{
    public string AgentVersionId { get; init; }      // GUID
    public string AgentDefinitionId { get; init; }
    public int Revision { get; init; }               // MAX+1
    public AgentVersionStatus Status { get; init; }  // Draft, Published, Retired
    public string? PromptContent { get; init; }      // Prompt congelado
    public string? PromptVersionId { get; init; }
    public AgentModelSnapshot Model { get; init; }
    public AgentProviderSnapshot Provider { get; init; }
    public IReadOnlyList<ToolFingerprint> ToolFingerprints { get; init; }
    public IReadOnlyList<AgentMiddlewareSnapshot> MiddlewarePipeline { get; init; }
    public IReadOnlyList<SkillRef> SkillRefs { get; init; }  // SkillVersionId resolvido
    public string ContentHash { get; init; }         // SHA-256 canônico
    public string? CreatedBy { get; init; }
    public string? ChangeReason { get; init; }
}
```

### Quando é Criada

**Automaticamente em TODO `UpsertAsync`** (create ou update do agente). O repositório faz dual-write:
1. Salva `AgentDefinition` (mutável) na tabela `agent_definitions`
2. Append `AgentVersion` (imutável) na tabela `agent_versions`

### Idempotência por ContentHash

```csharp
// PgAgentVersionRepository.AppendAsync()
var last = await GetLastRevision(agentId);
if (last?.ContentHash == version.ContentHash)
    return last;  // Não cria versão duplicada
// else: append nova revisão
```

Se o conteúdo não mudou (ex: update sem alteração real), nenhuma versão nova é criada.

### ContentHash — O que entra no cálculo

```
SHA256(JSON({
    agentId, prompt, model, provider, tools,
    middlewares, outputSchema, resilience,
    costBudget, skills
}))
```

### Rollback (Determinístico)

**Endpoint:** `POST /api/agents/{id}/rollback`

```json
{ "targetVersionId": "version-guid", "changeReason": "Revertendo para v3" }
```

**Fluxo:**

```
1. Fetch AgentVersion (snapshot alvo)
       ↓
2. RebuildFromSnapshot() — reconstrói AgentDefinition completa:
   - Model, Provider, Instructions (do PromptContent), Tools, Middlewares,
     StructuredOutput, Resilience, CostBudget, SkillRefs
       ↓
3. AgentService.UpdateAsync(rebuilt)
       ↓
4. UpsertAsync → AppendAsync verifica ContentHash
       ↓
5. Se hash == hash do alvo → idempotência (retorna versão existente)
   Se hash != (ex: skills evoluíram) → cria nova revisão
```

**Garantia:** Rollback é determinístico porque o snapshot captura estado completo incluindo `SkillVersionId` materializado.

### Compare

**Endpoint:** `POST /api/agents/{id}/compare`

```json
{ "versionIdA": "v1-guid", "versionIdB": "v2-guid", "input": "teste" }
```

**Retorna:** Metadata das duas versões (Revision, ContentHash) para análise. Execução side-by-side em sandbox é preparação futura.

### API

| Endpoint | Descrição |
|----------|-----------|
| `GET /api/agents/{id}/versions` | Lista versões (Revision DESC) |
| `POST /api/agents/{id}/rollback` | Restaura de snapshot |
| `POST /api/agents/{id}/compare` | Compara duas versões |

---

## 12. Versionamento de Prompt

### Modelo

```csharp
public record AgentPromptVersion(
    string VersionId,    // "v1", "v1.2", "v1-upd20260420153045"
    string Content,      // Texto do system prompt
    bool IsActive        // Se é a versão "master"
);
```

### Storage

- **PostgreSQL:** `agent_prompt_versions` (AgentId, VersionId, Content, IsActive, CreatedAt)
- **Redis Cache:** `efs-ai-hub:agent-prompt:active:{agentId}` com TTL 5min (configurable)
- **Read-through:** Miss no Redis → query DB → cache resultado

### Auto-Seed na Criação do Agente

```
AgentService.CreateAsync(definition)
  └─ SeedInitialPromptAsync()
     ├─ Se Instructions presente E nenhuma versão existe:
     ├─ SaveVersionAsync(agentId, "v1", instructions)
     └─ SetMasterAsync(agentId, "v1")
```

### Sync Automático no Update

```
AgentService.UpdateAsync(definition com novo Instructions)
  ├─ Detecta: instructions != existing.instructions
  ├─ Gera versionId: "{currentVersion}-upd{yyyyMMddHHmmss}"
  ├─ SaveVersionAsync(agentId, newVersionId, newInstructions)
  └─ SetMasterAsync(agentId, newVersionId)
     ├─ DB: deactivate all, activate new
     └─ Redis: update ou invalidate cache
```

### Gerenciamento Manual (API)

| Endpoint | Método | Descrição |
|----------|--------|-----------|
| `/api/agents/{id}/prompts` | GET | Lista todas as versões com flag IsActive |
| `/api/agents/{id}/prompts/active` | GET | Retorna conteúdo da versão ativa |
| `/api/agents/{id}/prompts` | POST | Cria/atualiza versão `{ versionId, content }` |
| `/api/agents/{id}/prompts/master` | PUT | Ativa versão `{ versionId }` |
| `/api/agents/{id}/prompts/master` | DELETE | Desativa todas (fallback: Instructions base) |
| `/api/agents/{id}/prompts/{versionId}` | DELETE | Remove versão não-ativa |

### Invalidação de Cache

| Operação | Comportamento |
|----------|---------------|
| SetMaster | `SetIfExistsAsync` no Redis (update se key existe) |
| SaveVersion (active) | `InvalidateCache` (remove key) |
| DeleteVersion | Remove do DB (proíbe deletar active) |
| ClearMaster | Remove key do Redis explicitamente |

### Rastreamento em Execuções

```
AgentFactory.ResolveActivePrompt(definition)
  ├─ GetActivePromptWithVersionAsync(agentId)
  │  └─ Redis cache hit ou DB fallback
  ├─ Registra: ExecutionContext.PromptVersions[agentId] = versionId
  └─ Substitui definition.Instructions pelo conteúdo resolvido

TokenTrackingChatClient (a cada chamada LLM):
  └─ LlmTokenUsage.PromptVersionId = ctx.PromptVersions[agentId]
```

Resultado: toda chamada LLM tem `PromptVersionId` no audit trail → reprodutibilidade.

---

## 13. Sandbox

### Conceito

Modo de execução isolado para **testar agentes sem efeitos colaterais**:
- LLM executa normalmente (real)
- Tools são mockadas (não executam código real)
- ChatMessages não são persistidas
- Métricas tagueadas com `mode=sandbox`
- TokenUsage rastreado separadamente

### ExecutionMode

```csharp
public enum ExecutionMode
{
    Production,  // Default: tudo real
    Sandbox      // Tools mockadas, sem persistência de chat
}
```

### ToolMocker

```
src/EfsAiHub.Platform.Runtime/Sandbox/ToolMocker.cs
```

```csharp
public static class ToolMocker
{
    public static List<AITool> MockTools(IList<AITool> tools, IReadOnlySet<string>? toolsToMock = null)
    {
        // Se toolsToMock é null → ALL tools mockadas
        // Senão → apenas as listadas são mockadas
    }
}

public sealed class MockedAIFunction : AIFunction
{
    // Retorna JSON sem executar a função real:
    // { "_mocked": true, "tool": "nome", "message": "[SANDBOX] Tool called...", "arguments": {...} }
}
```

### API Endpoints

| Endpoint | Descrição |
|----------|-----------|
| `POST /api/agents/{id}/sandbox` | Testa agente isolado com input |
| `POST /api/agents/{id}/compare` | Compara duas versões (sandbox) |
| `POST /api/workflows/{id}/sandbox` | Executa workflow em sandbox |

### Request

```json
POST /api/agents/{id}/sandbox
{
  "input": "Analise PETR4",
  "mockTools": ["buscar_ativo"]  // Opcional: apenas estas são mockadas
}
```

### Limites

- `ProjectSettings.MaxSandboxTokensPerDay`: default 50.000 tokens
- Budget separado de produção

### Status de Implementação

| Componente | Status |
|-----------|--------|
| ExecutionMode enum | Implementado |
| ToolMocker + MockedAIFunction | Implementado |
| API endpoints | Implementado |
| Mode no ExecutionRequest | Implementado |
| Propagação para ExecutionContext | Parcial (gap no dispatcher) |
| ChatMessage persistence guard | Pendente |
| IsSandbox flag no TokenUsage | Pendente |

---

## 14. Human-in-the-Loop (HITL)

### Configuração Declarativa (por nó)

```csharp
public class NodeHitlConfig
{
    public required string When { get; init; }      // "before" | "after"
    public InteractionType InteractionType { get; init; } = InteractionType.Approval;
    public required string Prompt { get; init; }    // Pergunta ao humano
    public bool ShowOutput { get; init; } = false;
    public IReadOnlyList<string>? Options { get; init; }
    public int TimeoutSeconds { get; init; } = 300;
}
```

### Tipos de Interação

| Tipo | Input esperado |
|------|----------------|
| `Approval` | Aprovar/Rejeitar (binário) |
| `Input` | Texto livre |
| `Choice` | Uma das N opções predefinidas |

### Ativação

1. `WorkflowConfiguration.EnableHumanInTheLoop = true`
2. Adicionar `Hitl` ao `WorkflowAgentReference`:

```json
{
  "agentId": "executor-boleta",
  "hitl": {
    "when": "after",
    "interactionType": "Approval",
    "prompt": "Confirma a ordem?",
    "showOutput": true,
    "timeoutSeconds": 120
  }
}
```

### Fluxo

```
WorkflowFactory wraps executor com HitlDecoratorExecutor
    ↓
when="before": Pausa ANTES da execução
when="after":  Executa → Pausa DEPOIS
    ↓
IHumanInteractionService.RequestAsync() — bloqueia via TaskCompletionSource
    ↓
Evento "hitl_required" → SSE → Frontend mostra UI de aprovação
    ↓
Humano responde → HitlResolutionClassifier → Resolve
    ↓
Aprovado: workflow continua | Rejeitado: HitlRejectedException
```

### Cross-Pod

- `PgCrossNodeBus` publica resolução via `pg_notify("efs_hitl_resolved")`
- `HitlRecoveryService` (30s poll) re-registra TCS para Paused após restart

---

## 15. Escalation e Roteamento

Mecanismo **ortogonal ao HITL** — agente emite sinal para roteamento dinâmico no grafo.

```csharp
public sealed class AgentEscalationSignal
{
    public required string Reason { get; init; }
    public required string Category { get; init; }         // "billing", "support"
    public IReadOnlyList<string> SuggestedTargetTags { get; init; } = [];
    public double Confidence { get; init; } = 1.0;
    public string? Payload { get; init; }
}
```

### Routing Rules (no WorkflowConfiguration)

```json
{
  "routingRules": [
    { "match": "category:billing", "targetNodeId": "agent-billing", "priority": 10 },
    { "match": "tag:escalate", "targetNodeId": "agent-supervisor", "priority": 5 },
    { "match": "regex:^refund.*", "targetNodeId": "agent-refund" }
  ]
}
```

### Diferença HITL vs Escalation

| | HITL | Escalation |
|---|------|------------|
| Bloqueia? | Sim (TaskCompletionSource) | Não |
| Quem decide? | Humano | Router automático |
| Resultado | Aprovação/Rejeição | Redirect para outro nó |
| Configuração | `NodeHitlConfig` | `RoutingRule` |

---

## 16. Observabilidade

### 4 ActivitySources (OpenTelemetry)

| Source | Span | Tags |
|--------|------|------|
| WorkflowExecution | "WorkflowRun" | workflow.id, execution.id |
| AgentInvocation | "AgentInvocation" | agent.id, agent.name |
| LlmCall | "LLMCall" | model.id, tokens.*, cost.*, duration.ms |
| ToolCall | "ToolCall" | tool.name, duration_ms, success |

### Métricas Relevantes

- `AgentTokensUsed` — histogram por agent_id, model_id
- `AgentCostUsd` — histogram de custo
- `AgentInvocationDuration` — histogram
- `ToolInvocationsByFingerprint` — counter por tool, fingerprint
- `CircuitBreakerOpened/Fallbacks/Rejected` — counters por provider
- `HitlRequested/Resolved/ResolutionDuration` — counters + histogram
- `EscalationSignalsTotal` — counter por category, routed
- `BackgroundResponseCallbacks` — counter por outcome

---

## 17. Enrichment e Disclaimers

### EnrichmentRule

```json
{
  "enrichmentRules": [
    {
      "when": { "responseType": "trade_signal" },
      "appendDisclaimer": "regulatorio_cvm",
      "defaults": { "account": "from_context" }
    }
  ]
}
```

### DisclaimerRegistry

```csharp
["regulatorio_cvm"] = "⚠️ Valores estimados com base em conhecimento histórico. Consulte dados em tempo real antes de operar."
```

---

## 18. Rate Limiting e Concorrência

### Per-User Rate Limiting (Chat)

| Config | Default | Descrição |
|--------|---------|-----------|
| `MaxMessages` | 10 | Msgs por janela |
| `WindowSeconds` | 60 | Janela sliding |
| `MaxMessagesPerConversation` | 5 | Per-conversation limit |
| `ConversationWindowSeconds` | 60 | Janela per-conversation |

Implementado via Redis Sorted Set + Lua script atômico (distributed).

### Concorrência de Workflows

| Config | Default | Descrição |
|--------|---------|-----------|
| `WorkflowConfiguration.MaxConcurrentExecutions` | null | Semáforo per-workflow |
| `ChatMaxConcurrentExecutions` | 200 | Slots globais Chat (Redis) |
| `MaxConcurrentExecutions` (global) | 10 | Per-dispatcher |
| `QueueCapacity` | 500 | Tamanho da fila |

### Back-Pressure

- Slot check via Redis Lua ANTES de criar execution
- Se excedido: `ChatBackPressureException` → HTTP 429
- Slot TTL 5min: auto-reclaim se pod crashar

---

## 19. Background Jobs e Webhooks

### Background Response Jobs

Para execuções assíncronas com callback:

```csharp
public class BackgroundResponseJob
{
    public string JobId { get; init; }
    public string AgentId { get; init; }
    public string? SessionId { get; init; }
    public string Input { get; init; }
    public string? Output { get; set; }
    public string? LastError { get; set; }
    public int Attempt { get; set; }
    public ResponseCallbackTarget? CallbackTarget { get; init; }
    public string? IdempotencyKey { get; init; }
}
```

### Webhook Callback

```csharp
public class ResponseCallbackTarget
{
    public required string Url { get; init; }
    public string? HmacSecret { get; init; }      // HMAC-SHA256 signing
    public Dictionary<string, string> Headers { get; init; }
}
```

**Headers emitidos:**
- `X-EfsAiHub-JobId`: ID do job
- `X-EfsAiHub-Event`: `background_response.completed`
- `X-EfsAiHub-Signature`: `sha256={hex}` (se HmacSecret configurado)

**Retry:** 3 tentativas com backoff exponencial (2s, 4s).

---

## 20. Knowledge Sources (RAG)

### Interface (Phase 3-4)

```csharp
public interface IKnowledgeSource
{
    Task<IReadOnlyList<RetrievedDocument>> RetrieveAsync(RetrievalQuery query, CancellationToken ct);
}

public class RetrievalQuery
{
    public required string Text { get; init; }
    public int TopK { get; init; } = 5;
    public double MinSimilarity { get; init; } = 0.7;
    public Dictionary<string, string>? Filters { get; init; }
}
```

### Tipos Suportados

| Kind | Descrição |
|------|-----------|
| `Pgvector` | PostgreSQL com extensão pgvector |
| `AzureAiSearch` | Azure AI Search |
| `FoundryFileSearch` | Azure Foundry file_search |

### Integração com Agente

- `Skill.KnowledgeSourceIds` referencia fontes de conhecimento
- `ExecutionContext.RetrievedDocuments` injeta documentos no contexto
- Métricas: `RagRetrievalLatency`, `RagDocsReturned`
- **Status:** Interfaces congeladas (Phase 3), implementação pendente (Phase 4)

---

## 21. Categorias de Erro

```csharp
public enum ErrorCategory
{
    Unknown,                  // Não classificado
    Timeout,                  // Execução excedeu timeout
    AgentLoopLimit,          // Ping-pong detectado
    LlmError,                // HTTP 5xx do LLM
    LlmRateLimit,            // HTTP 429
    LlmContentFilter,        // Safety filter bloqueou
    ToolError,                // Exceção em tool
    Cancelled,               // Cancelada
    FrameworkError,           // Erro interno do framework
    BackPressureRejected,    // Slot limit excedido
    BudgetExceeded,          // MaxTokens ou MaxCostUsd
    CheckpointRecoveryFailed, // Falha ao restaurar checkpoint
    HitlRejected             // Humano rejeitou
}
```

---

## 22. Como Criar um Agente

### 1. Defina via API

```bash
curl -X PUT /api/agents/meu-agente -d '{
  "id": "meu-agente",
  "name": "Meu Agente",
  "model": { "deploymentName": "gpt-4o", "temperature": 0.7 },
  "provider": { "type": "AzureOpenAI" },
  "instructions": "Você é um assistente...",
  "tools": [{ "type": "function", "name": "minha_tool" }],
  "middlewares": [{ "type": "AccountGuard", "settings": { "mode": "ClientLocked" } }]
}'
```

### 2. Implemente e registre tools

```csharp
public class MinhasFunctions
{
    [Description("Faz algo útil")]
    public async Task<string> MinhaFunction([Description("Param")] string param)
        => "resultado";
}

// Program.cs:
registry.Register("minha_tool", AIFunctionFactory.Create(funcs.MinhaFunction));
```

### 3. (Opcional) Crie Skills reutilizáveis

```bash
curl -X PUT /api/skills/skill-mercado -d '{
  "id": "skill-mercado",
  "name": "Dados de Mercado",
  "instructionsAddendum": "Use as tools de mercado quando solicitado.",
  "tools": [{ "type": "function", "name": "buscar_ativo" }]
}'
```

### 4. Use em Workflow

```json
{
  "mode": "Sequential",
  "configuration": { "enableHumanInTheLoop": true },
  "agents": [{
    "agentId": "meu-agente",
    "hitl": { "when": "after", "prompt": "Confirma?", "interactionType": "Approval" }
  }]
}
```

### 5. Ou use Standalone

```bash
curl -X POST /api/agents/meu-agente/sessions
# → { "sessionId": "sess-123" }

curl -X POST /api/agents/meu-agente/sessions/sess-123/run \
  -d '{ "message": "Analise PETR4" }'
# → { "response": "...", "turnCount": 1 }
```

### 6. Teste em Sandbox

```bash
curl -X POST /api/agents/meu-agente/sandbox \
  -d '{ "input": "Analise PETR4", "mockTools": null }'
```

### Validações na Criação

| Regra | Detalhe |
|-------|---------|
| Id/Name | Obrigatórios, Name ≤ 200 chars |
| DeploymentName | Obrigatório |
| Temperature | 0.0–2.0 |
| Provider.Type | AzureFoundry \| AzureOpenAI \| OpenAI |
| Tools function | Name obrigatório, nomes únicos |
| Tools MCP | ServerLabel + ServerUrl + AllowedTools, URL HTTPS + health check |
| StructuredOutput | Schema obrigatório para `json_schema` |

---

## 23. Agentes Cadastrados

### `classificador-fato-relevante`

| Campo | Valor |
|-------|-------|
| **Name** | Classificador de Fato Relevante |
| **Model** | gpt-5.4-mini (OpenAI) |
| **Temperature** | 0.1 |
| **MaxTokens** | 2000 |
| **StructuredOutput** | `json_schema` — schema `FatoRelevanteClassification` |
| **Tools** | nenhuma |
| **Middlewares** | nenhum |
| **Domain** | capital-markets |
| **Category** | document-analysis |

**Schema `FatoRelevanteClassification`:**

| Campo | Tipo | Descrição |
|-------|------|-----------|
| `score` | number (0–10) | Score de relevância |
| `categoria` | enum | `Aquisicao`, `Alienacao`, `Dividendos`, `Reorganizacao`, `Endividamento`, `Governanca`, `Regulatorio`, `Operacional`, `Juridico`, `Outro` |
| `resumo` | string | Resumo do fato relevante |

```json
PUT /api/agents/classificador-fato-relevante
{
  "id": "classificador-fato-relevante",
  "name": "Classificador de Fato Relevante",
  "model": { "deploymentName": "gpt-5.4-mini", "temperature": 0.1, "maxTokens": 2000 },
  "provider": { "type": "OpenAI" },
  "instructions": "Classifique o fato relevante extraído do PDF...",
  "tools": [],
  "middlewares": [],
  "structuredOutput": {
    "type": "json_schema",
    "schemaName": "FatoRelevanteClassification",
    "schema": {
      "type": "object",
      "properties": {
        "score": { "type": "number", "minimum": 0, "maximum": 10 },
        "categoria": {
          "type": "string",
          "enum": ["Aquisicao", "Alienacao", "Dividendos", "Reorganizacao", "Endividamento", "Governanca", "Regulatorio", "Operacional", "Juridico", "Outro"]
        },
        "resumo": { "type": "string" }
      },
      "required": ["score", "categoria", "resumo"]
    }
  },
  "domain": "capital-markets",
  "category": "document-analysis"
}
```

---

## 24. Personalização por Usuário (Persona)

Cada usuário autenticado pode ter uma **Persona** resolvida automaticamente a partir do UserId do header. A persona personaliza o prompt do agente sem alterar o prompt store — nenhuma combinação N×M de prompts por segmento.

> **Atualização pós-MVP:** o modelo canônico agora é **discriminado** em
> `ClientPersona` (cliente final) e `AdminPersona` (assessor/gestor/consultor/padrão),
> com shapes distintos espelhando os dois endpoints da API externa.
> Ver `src/EfsAiHub.Core.Abstractions/Identity/Persona/UserPersona.cs` para o
> modelo corrente. Os campos listados abaixo são históricos.

### Campos históricos (pré-redesenho cliente/admin)

| Campo | Descrição | Usado por |
|---|---|---|
| `DisplayName` | Nome exibível ("João Silva") | `## Persona` no system message |
| `Segment` | `varejo \| corporativo \| institucional \| private` | `TonePolicyTable` lookup |
| `RiskProfile` | `conservador \| moderado \| agressivo` | `TonePolicyTable` lookup |
| `AdvisorId` | ID do assessor responsável | `## Persona` (rastreabilidade) |

### Campos correntes

**ClientPersona:** `ClientName`, `SuitabilityLevel`, `SuitabilityDescription`,
`BusinessSegment`, `Country`, `IsOffshore`.

**AdminPersona:** `Username`, `PartnerType` (DEFAULT|CONSULTOR|GESTOR|ADVISORS),
`Segments[]`, `Institutions[]`, `IsInternal`, `IsWm`, `IsMaster`, `IsBroker`.

### OpenAI prompt caching (F1)

O bloco de persona é inserido **depois** das instructions do agente no system
message — garante prefixo invariante, que é o que o cache da OpenAI chaveia.
Com gpt-5.x, cache hit significa ~10% do custo full em input tokens
(75-90% off).

- **Medir**: coluna `CachedTokens` em `aihub.llm_token_usage` (via
  `UsageDetails.CachedInputTokenCount`). Ver [docs/observabilidade.md](observabilidade.md)
  e [ADR 000](adr/000-opensdk-shape.md).
- **Não controlamos `prompt_cache_key`** ainda (bloqueio no SDK —
  [openai-dotnet#641](https://github.com/openai/openai-dotnet/issues/641)).
  Mitigação: prefixo invariante garante cache hit provável, não garantido.

### Fluxo de resolução

1. `UserIdentityResolver` extrai `UserId` + `UserType` do header.
2. `PersonaResolutionMiddleware` chama `PersonaResolutionService` → `IPersonaProvider`.
3. Provider real (`HttpPersonaProvider`) consulta API externa com fallback silencioso para `UserPersona.Anonymous` em qualquer erro (contrato: nunca lança).
4. Decorator `CachedPersonaProvider`: L1 in-memory (60s) → Redis (5min) → API.
5. `IPersonaContextAccessor.Current` populado no scope HTTP.
6. `WorkflowRunnerService` resolve novamente no worker (workflows schedule/webhook sem HTTP) e passa ao `ExecutionContext.Persona`.
7. `AgentFactory` chama `IPersonaPromptComposer` → `SystemMessageBuilder` concatena prompt base + bloco de persona.

### Estrutura das mensagens enviadas ao LLM

```
SystemMessage (única, com Markdown sections):
=============================================
<Instructions do agente — invariante, compõe o prefixo cacheável do OpenAI>

## Persona do cliente
- Segment: private
- Risk profile: conservador
- Display name: João Silva
- Advisor: A123

## Tone Policy
Formal e técnico. Sugestões somente de renda fixa grau de investimento...
=============================================

SystemMessage (sessão metadata existente)
AssistantMessages[] (histórico)
UserMessage: <msg atual>

[persona.segment=private, persona.risk=conservador]
```

**Por que 1 system message unificada** (e não 2 separadas): docs OpenAI oficiais recomendam instruction única com seções internas; cache de prompt é por prefixo exato até primeiro token divergente — colocar persona **depois** do prompt base preserva ~90% de desconto em cache hit com gpt-5.x.

**Por que Markdown e não XML**: GPT-5 Prompting Guide oficial OpenAI (ago/2025) favorece Markdown headers. GPT-5 respeita ambos mas Markdown é idiomático na stack.

**Por que reforço curto no user message**: combate lost-in-the-middle (Liu et al. 2024). Apenas ~15 tokens ancoram o "last-token bias" sem inflar cada turno.

### `TonePolicyTable` — policies por `(Segment × RiskProfile)`

Tabela hardcoded em `src/EfsAiHub.Platform.Runtime/Personalization/TonePolicyTable.cs`. 4 segments × 3 risk profiles = 12 linhas auditáveis.

Adicionar novo segment ou perfil = 1 linha na tabela + PR com revisão (compliance-friendly: texto exato que orienta recomendações financeiras fica em arquivo C# reviewable).

### Fallback silencioso

Qualquer ponto onde persona não resolve cai em `UserPersona.Anonymous` e o agente usa só o prompt base. Comportamento idêntico ao pré-feature.

Configuração em `appsettings.json`:
```json
"Persona": {
  "BaseUrl": "https://persona-api.example.com",
  "ApiKey": "<secret>",
  "AuthScheme": "Bearer",
  "TimeoutSeconds": 3,
  "CacheTtlMinutes": 5,
  "LocalCacheTtlSeconds": 60,
  "Disabled": false
}
```

`Disabled: true` (default em dev) desliga a chamada — todas personas ficam Anonymous.

### Observabilidade

- Métrica `persona.resolution.duration_ms` (Histogram, tag `outcome=cache_hit_l1|cache_hit_l2|api_hit|fallback`)
- Métrica `persona.resolution.failures` (Counter) — API indisponível
- Métrica `persona.prompt.compose.chars` (Histogram) — detecta inchaço acidental
- Endpoint admin: `GET /api/admin/personas/{userId}` (debug) e `POST /api/admin/personas/{userId}/invalidate` (LGPD/refresh)

### Não-objetivos (backlog)

- Prompt variants por segment (explosão combinatória — rejeitado)
- Template engine (Scriban/Handlebars — rejeitado como "linguagem paralela")
- Frontend admin de personas (API externa é fonte de verdade)
- Integração com role `developer` da OpenAI (espera suporte formal em `Microsoft.Agents.AI`)

---

## Apêndice: Diagrama de Relacionamentos

```
┌──────────────┐       ┌──────────────┐       ┌──────────────┐
│   Project    │──1:N──│    Agent     │──1:N──│ AgentVersion │
│  (tenant)    │       │ Definition   │       │  (snapshot)  │
└──────────────┘       └──────┬───────┘       └──────────────┘
                              │
                   ┌──────────┼──────────┬─────────────┐
                   │          │          │             │
              ┌────▼───┐ ┌───▼────┐ ┌───▼────┐  ┌────▼─────┐
              │  Tools │ │Middles │ │SkillRef│  │  Prompt  │
              │  (N)   │ │  (N)  │ │  (N)   │  │ Versions │
              └────────┘ └────────┘ └───┬────┘  └──────────┘
                                        │
                              ┌─────────▼─────────┐
                              │       Skill       │──1:N── SkillVersion
                              └─────────┬─────────┘
                              ┌─────────┼─────────┐
                              │         │         │
                         ┌────▼──┐ ┌────▼────┐ ┌──▼───────────┐
                         │ Tools │ │ Instrs  │ │ Knowledge    │
                         │  (N)  │ │ Addend  │ │ SourceIds(N) │
                         └───────┘ └─────────┘ └──────────────┘
```

---

## Apêndice: Glossário

| Termo | Significado |
|-------|-------------|
| **AgentDefinition** | Configuração declarativa do agente (mutável) |
| **AgentVersion** | Snapshot imutável com ContentHash (append-only) |
| **AgentPromptVersion** | Versão do system prompt (master pointer, cache Redis) |
| **AIAgent** | Instância executável (Microsoft.Agents.AI framework) |
| **DelegateExecutor** | Wrapper para execução em Graph mode |
| **ExecutionContext** | Estado propagado via AsyncLocal durante execução |
| **TrackedAIFunction** | Decorator: tracking + AccountGuard nas tools |
| **MockedAIFunction** | Wrapper sandbox: retorna JSON fake sem executar |
| **FunctionInvokingChatClient** | Auto-invoca tools quando LLM as solicita |
| **Skill** | Pacote reutilizável de tools + instruções + policy |
| **SkillRef** | Referência com version lock opcional |
| **ContentHash** | SHA-256 canônico para idempotência |
| **AG-UI** | Protocolo SSE para streaming real-time |
| **HITL** | Human-in-the-Loop: aprovação humana bloqueante |
| **HitlDecoratorExecutor** | Decorator que adiciona HITL a qualquer executor |
| **NodeHitlConfig** | Configuração declarativa de HITL por nó |
| **EscalationSignal** | Sinal para roteamento dinâmico (não-bloqueante) |
| **RoutingRule** | Regra declarativa de matching/redirecionamento |
| **Circuit Breaker** | Closed → Open (5 falhas) → HalfOpen (30s) |
| **ToolMocker** | Sandbox: wraps tools com MockedAIFunction |
| **PredictiveState** | UI otimista via tool args → state updates |
| **EnrichmentRule** | Pós-processamento declarativo (disclaimers, defaults) |
| **BackgroundResponseJob** | Execução assíncrona com webhook callback |
