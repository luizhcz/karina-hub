# AG-UI Protocol — Guia Completo

> Como o protocolo AG-UI funciona no EfsAiHub: endpoints, eventos, streaming SSE, estado compartilhado, reconexão, HITL e o fluxo completo do chat.

---

## Sumário

1. [O que é o AG-UI](#1-o-que-é-o-ag-ui)
2. [Endpoints](#2-endpoints)
3. [Protocolo SSE (Wire Format)](#3-protocolo-sse-wire-format)
4. [Catálogo de Eventos](#4-catálogo-de-eventos)
5. [Modelo de Evento (AgUiEvent)](#5-modelo-de-evento-aguievent)
6. [Fluxo Completo do Chat](#6-fluxo-completo-do-chat)
7. [Conversação e Sessão](#7-conversação-e-sessão)
8. [Histórico e Contexto de Turno](#8-histórico-e-contexto-de-turno)
9. [Streaming de Tokens](#9-streaming-de-tokens)
10. [Tool Calls e Tool Results](#10-tool-calls-e-tool-results)
11. [Estado Compartilhado (Shared State)](#11-estado-compartilhado-shared-state)
12. [Predictive State](#12-predictive-state)
13. [HITL via AG-UI](#13-hitl-via-ag-ui)
14. [Reconexão e Replay](#14-reconexão-e-replay)
15. [Cancelamento](#15-cancelamento)
16. [Grace Period de Desconexão](#16-grace-period-de-desconexão)
17. [Deduplicação (SequenceId)](#17-deduplicação-sequenceid)
18. [Merge de Streams](#18-merge-de-streams)
19. [Frontend Tools](#19-frontend-tools)
20. [Rate Limiting](#20-rate-limiting)
21. [Concorrência por Conversa](#21-concorrência-por-conversa)
22. [Persistência de Mensagens](#22-persistência-de-mensagens)
23. [Mapeamento de Eventos Internos](#23-mapeamento-de-eventos-internos)
24. [Pipeline de Middleware HTTP](#24-pipeline-de-middleware-http)
25. [Configuração](#25-configuração)
26. [Tratamento de Erros](#26-tratamento-de-erros)

---

## 1. O que é o AG-UI

AG-UI (Agent User Interface) é o protocolo de streaming real-time do EfsAiHub. Ele conecta a execução de workflows/agentes ao frontend via **Server-Sent Events (SSE)**, entregando:

- Tokens de LLM em tempo real
- Lifecycle de runs e steps
- Tool calls com argumentos em streaming
- Estado compartilhado (JSON Patch RFC 6902)
- Interações humanas (HITL) inline
- Reconexão com replay sem perda de eventos

### Arquitetura

```
Frontend (Browser/App)
    │
    │ POST /api/chat/ag-ui/stream
    │ ← SSE (text/event-stream)
    │
    ▼
┌──────────────────────────────────────────┐
│ AgUiEndpoints → AgUiSseHandler           │
│   ├─ AgUiStreamMerger                    │
│   │   ├─ PgEventBus (lifecycle, HITL)    │  ← PostgreSQL LISTEN/NOTIFY
│   │   └─ AgUiTokenChannel (tokens)       │  ← In-memory bounded channel
│   ├─ AgUiEventMapper                     │
│   ├─ AgUiStateManager (L1+L2)           │  ← MemoryCache + Redis
│   └─ AgUiApprovalMiddleware              │
└──────────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────────┐
│ ConversationFacade → ConversationService │
│   → WorkflowService.TriggerAsync()      │
│     → WorkflowRunnerService (execução)   │
└──────────────────────────────────────────┘
```

---

## 2. Endpoints

```
src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEndpoints.cs
```

| Método | Path | Content-Type | Descrição |
|--------|------|-------------|-----------|
| POST | `/api/chat/ag-ui/stream` | `text/event-stream` | Inicia run + abre stream SSE |
| POST | `/api/chat/ag-ui/cancel` | `application/json` | Cancela execução em andamento |
| POST | `/api/chat/ag-ui/resolve-hitl` | `application/json` | Resolve HITL sem novo stream |
| GET | `/api/chat/ag-ui/reconnect/{executionId}` | `text/event-stream` | Reconexão com replay |

### POST /stream — Request

```csharp
public sealed record AgUiRunInput
{
    public string? ThreadId { get; init; }                        // Conversa; null = criar nova
    public string? WorkflowId { get; init; }                      // Workflow; null = header x-efs-workflow-id
    public string? RunId { get; init; }                           // ID do run (propagado nos eventos)
    public IReadOnlyList<AgUiInputMessage>? Messages { get; init; } // Histórico + aprovações HITL
    public AgUiFrontendTool[]? Tools { get; init; }               // Tools do frontend
    public JsonElement? State { get; init; }                      // Estado inicial do frontend
    public JsonElement? Context { get; init; }                    // Contexto adicional
    public PredictiveStateConfig? PredictiveState { get; init; }  // Mapeamento tool → state
}
```

### AgUiInputMessage

```csharp
public sealed record AgUiInputMessage(
    string Role,          // "user" | "assistant" | "tool"
    string Content,       // Texto da mensagem
    string? ToolCallId    // Para role=tool: ID da interação HITL resolvida
);
```

### POST /cancel — Request

```csharp
public sealed record CancelRunRequest(string ExecutionId);
```

### POST /resolve-hitl — Request

```csharp
public sealed record HitlResolveRequest(
    string ToolCallId,    // interactionId da HITL
    string Response       // "approved" | "rejected" | texto livre
);
```

---

## 3. Protocolo SSE (Wire Format)

### Headers de Resposta

```http
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive
X-Accel-Buffering: no
```

O header `X-Accel-Buffering: no` desabilita buffering no nginx para garantir entrega imediata.

### Formato do Evento

Cada evento SSE segue o formato padrão:

```
id: <sequenceId>
data: <JSON>

```

- **`id:`** — SequenceId do PgEventBus (0 para tokens não persistidos). Usado pelo browser para `Last-Event-ID` em reconexão.
- **`data:`** — Objeto JSON `AgUiEvent` serializado.
- Linha vazia final obrigatória (delimitador SSE).

### Exemplo Real

```
id: 12345
data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"msg-abc","delta":"Olá","timestamp":"2026-04-21T10:30:00Z"}

id: 12346
data: {"type":"TEXT_MESSAGE_CONTENT","messageId":"msg-abc","delta":", como posso","timestamp":"2026-04-21T10:30:00.1Z"}

id: 12347
data: {"type":"TEXT_MESSAGE_END","messageId":"msg-abc","timestamp":"2026-04-21T10:30:00.2Z"}

```

### Escrita no Servidor

```csharp
await response.WriteAsync($"id: {sequenceId}\n", ct);
await response.WriteAsync($"data: {json}\n\n", ct);
await response.Body.FlushAsync(ct);
```

Flush imediato após cada evento para latência mínima.

---

## 4. Catálogo de Eventos

O AG-UI define **16 tipos de evento** organizados em 7 categorias:

### Run Lifecycle (3)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `RUN_STARTED` | runId, threadId | Workflow iniciou execução |
| `RUN_FINISHED` | output | Workflow completou com sucesso |
| `RUN_ERROR` | error, errorCode | Workflow falhou/cancelou/timeout |

### Step Lifecycle (2)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `STEP_STARTED` | stepId, stepName | Nó (agente/executor) iniciou |
| `STEP_FINISHED` | stepId, stepName | Nó completou |

### Text Messages (3)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `TEXT_MESSAGE_START` | messageId, role | Início de mensagem de texto |
| `TEXT_MESSAGE_CONTENT` | messageId, delta | Delta de token (streaming) |
| `TEXT_MESSAGE_END` | messageId | Fim da mensagem |

### Tool Calls (4)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `TOOL_CALL_START` | toolCallId, toolCallName, parentMessageId | Início de invocação de tool |
| `TOOL_CALL_ARGS` | toolCallId, toolCallName, delta | Argumentos parciais (streaming) |
| `TOOL_CALL_END` | toolCallId | Fim da invocação (sem result) |
| `TOOL_CALL_RESULT` | toolCallId, messageId, role, result | Resultado da execução |

### State (2)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `STATE_SNAPSHOT` | snapshot | Estado completo (JSON) |
| `STATE_DELTA` | delta | Diff incremental (JSON Patch RFC 6902) |

### Resync (1)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `MESSAGES_SNAPSHOT` | messages[] | Snapshot de mensagens para reconexão |

### Custom (1)

| Tipo | Campos | Descrição |
|------|--------|-----------|
| `CUSTOM` | customName, customValue | Evento extensível (ex: ESCALATION) |

---

## 5. Modelo de Evento (AgUiEvent)

```
src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiEvent.cs
```

```csharp
public sealed record AgUiEvent
{
    public required string Type { get; init; }

    // Lifecycle
    public string? RunId { get; init; }
    public string? ThreadId { get; init; }

    // Steps
    public string? StepId { get; init; }
    public string? StepName { get; init; }

    // Messages
    public string? MessageId { get; init; }
    public string? Role { get; init; }

    // Output
    public string? Output { get; init; }

    // Tool calls
    public string? ToolCallId { get; init; }
    public string? ToolCallName { get; init; }
    public string? Result { get; init; }
    public string? ParentMessageId { get; init; }

    // State
    public JsonElement? Snapshot { get; init; }
    public JsonElement? Delta { get; init; }

    // Resync
    public AgUiMessage[]? Messages { get; init; }

    // Custom
    public string? CustomName { get; init; }
    public JsonElement? CustomValue { get; init; }

    // Errors
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    // Metadata
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public long BusSequenceId { get; init; }  // Apenas para SSE id:, não serializado
}
```

Campos nulos são omitidos na serialização JSON (`JsonIgnoreCondition.WhenWritingNull`).

### AgUiMessage (Resync)

```csharp
public sealed record AgUiMessage(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt
);
```

---

## 6. Fluxo Completo do Chat

```
┌─────────────────────────────────────────────────────┐
│ 1. ENTRADA: POST /api/chat/ag-ui/stream             │
│    Body: { threadId, messages: [{role:"user",...}] } │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 2. APPROVAL MIDDLEWARE                               │
│    Processa mensagens role="tool" com ToolCallId     │
│    → Resolve HITL pendente (se houver)               │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 3. CONVERSATION FACADE                               │
│    Rate limit (user + conversa + projeto)            │
│    Lock por conversa (SemaphoreSlim)                 │
│    Get/Create ConversationSession                    │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 4. CONVERSATION SERVICE                              │
│    Persiste mensagens no PostgreSQL                  │
│    Cancela execução anterior (se ativa)              │
│    Carrega histórico (MaxHistoryMessages=20)         │
│    Trim por token budget (MaxHistoryTokens)          │
│    Carrega SharedState do Redis                      │
│    Constrói ChatTurnContext                           │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 5. WORKFLOW SERVICE (Chat Path)                      │
│    Back-pressure check (IExecutionSlotRegistry)      │
│    Cria WorkflowExecution (status: Pending)          │
│    Task.Run() → execução direta (sem fila)           │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 6. WORKFLOW EXECUTION                                │
│    WorkflowFactory → constrói workflow               │
│    WorkflowRunnerService → event loop                │
│    Emite eventos → PgEventBus                        │
│    Streaming tokens → AgUiTokenChannel               │
└───────────────────────┬─────────────────────────────┘
                        ▼
┌─────────────────────────────────────────────────────┐
│ 7. SSE HANDLER                                       │
│    STATE_SNAPSHOT inicial                            │
│    Merge: EventBus + TokenChannel                    │
│    AgUiEventMapper → converte para AG-UI events      │
│    Escreve SSE com sequence ID + flush               │
│    Termina em RUN_FINISHED ou RUN_ERROR              │
└─────────────────────────────────────────────────────┘
```

### Timeline de Eventos Típica

```
← STATE_SNAPSHOT     (estado compartilhado inicial)
← RUN_STARTED        (workflow iniciou)
← STEP_STARTED       (agente A iniciou)
← TEXT_MESSAGE_START  (mensagem do agente)
← TEXT_MESSAGE_CONTENT (token "Olá")
← TEXT_MESSAGE_CONTENT (token ", a análise")
← TEXT_MESSAGE_CONTENT (token " mostra que...")
← TEXT_MESSAGE_END    (mensagem completa)
← TOOL_CALL_START     (agente invocou tool)
← TOOL_CALL_ARGS      (args parciais)
← TOOL_CALL_ARGS      (mais args)
← TOOL_CALL_END       (tool invocada)
← TOOL_CALL_RESULT    (resultado da tool)
← STATE_DELTA         (estado atualizado)
← STEP_FINISHED       (agente A completou)
← STEP_STARTED        (agente B iniciou)
← TEXT_MESSAGE_START   ...
← TEXT_MESSAGE_END     ...
← STEP_FINISHED       (agente B completou)
← RUN_FINISHED        (workflow completou com output final)
[stream encerra]
```

---

## 7. Conversação e Sessão

### ConversationSession

```
src/EfsAiHub.Core.Abstractions/Conversations/ConversationSession.cs
```

```csharp
ConversationSession
{
    ConversationId          // threadId do AG-UI
    UserId                  // do JWT/header
    UserType                // "cliente" | "assessor"
    WorkflowId              // workflow vinculado
    Title                   // auto-gerado da primeira mensagem
    ActiveExecutionId       // execução em andamento (null quando idle)
    LastActiveAgentId       // otimização para Handoff (entry point)
    ContextClearedAt        // ponto de reset do histórico
    Metadata                // key-value customizável
    CreatedAt, LastMessageAt
}
```

### Ciclo de Vida

1. **Criar:** `POST /stream` com `threadId=null` → cria sessão + retorna `threadId` no `RUN_STARTED`
2. **Reutilizar:** `POST /stream` com `threadId` existente → carrega sessão, append mensagem
3. **Limpar contexto:** `ClearContextAsync()` → mensagens antes de `ContextClearedAt` excluídas do contexto
4. **Deletar:** `DeleteAsync()` → cancela execução ativa + remove todas as mensagens

### ConversationFacade

```
src/EfsAiHub.Host.Api/Chat/Services/ConversationFacade.cs
```

Facade que encapsula: repositórios, lookup de workflow, rate limiting, lock por conversa.

Retorna `ConversationOperationStatus` (enum) que mapeia para HTTP status codes:
- `Success` → 200
- `NotFound` → 404
- `RateLimited` → 429
- `ValidationError` → 400

---

## 8. Histórico e Contexto de Turno

### ChatTurnContext

```csharp
ChatTurnContext
{
    UserId
    ConversationId
    Message                 // mensagem atual do usuário
    History                 // últimas N mensagens (trimmed por tokens)
    Metadata                // user info, startAgentId
    SharedState             // snapshot do estado compartilhado (Redis)
}
```

### Construção do Contexto

```
src/EfsAiHub.Host.Api/Chat/Services/ConversationService.Messaging.cs
```

1. `GetContextWindowAsync()` — busca últimas `MaxHistoryMessages` (default 20) desde `ContextClearedAt`
2. `TrimHistoryByTokenBudget()` — se `MaxHistoryTokens` definido, remove mensagens mais antigas até caber no budget
3. `IAgUiSharedStateWriter.GetSnapshotAsync()` — carrega estado compartilhado do Redis
4. Serializa tudo como JSON e envia como input do workflow

### Propagação para Agentes

O `ChatTurnContext` é serializado e passado como input do workflow. Cada agente dentro do workflow recebe:
- A mensagem atual
- O histórico trimado
- O snapshot do estado compartilhado
- Metadata do usuário

---

## 9. Streaming de Tokens

### O Bridge Middleware: TokenTrackingChatClient

```
src/EfsAiHub.Platform.Runtime/Factories/TokenTrackingChatClient.cs
```

O `TokenTrackingChatClient` é o **middleware-ponte** entre o pipeline de execução LLM e o protocolo AG-UI. Ele fica no pipeline de cada agente e tem responsabilidade dupla:

| Responsabilidade | Mecanismo |
|-----------------|-----------|
| **Budget enforcement** | Verifica `Budget.IsExceeded` antes de cada chamada LLM; acumula tokens e custo |
| **Bridge AG-UI** | Intercepta `FunctionCallContent` durante streaming e escreve no `IAgUiTokenSink` em tempo real |

Durante `GetStreamingResponseAsync()`, o middleware itera cada chunk do LLM:

```csharp
// TokenTrackingChatClient — linhas 91-102
if (content is FunctionCallContent fcc && _tokenSink is not null)
{
    var ctx = DelegateExecutor.Current.Value;
    if (ctx?.ExecutionId is { } execId)
    {
        var argsJson = JsonSerializer.Serialize(fcc.Arguments);
        _tokenSink.WriteToolCallArgs(execId, fcc.CallId, fcc.Name, argsJson);
    }
}
```

A injeção do `IAgUiTokenSink` é **nullable** — quando o middleware opera fora do contexto AG-UI (ex: background jobs sem SSE), o sink é null e o bridging é ignorado. Isso permite que o mesmo middleware funcione em todos os cenários.

### Posição no Pipeline

```
Request →
  RetryingChatClient
    → CircuitBreakerChatClient
      → [User Middlewares]
        → TokenTrackingChatClient  ← BRIDGE: intercepta FunctionCallContent → IAgUiTokenSink
          → FunctionInvokingChatClient
            → Raw LLM Provider
```

O `TokenTrackingChatClient` fica **entre os user middlewares e o FunctionInvokingChatClient**, garantindo que:
- Todo output do LLM passa por ele (inclusive tool call args)
- O budget é verificado antes da chamada
- Os chunks de tool args são emitidos para AG-UI em tempo real

### Dois Caminhos de Token

O streaming de tokens usa dois caminhos paralelos a partir do bridge middleware:

#### Caminho A: Token Deltas (texto via Event Bus)

```
LLM → TokenTrackingChatClient → WorkflowRunnerService (event loop)
    → WorkflowEventBus.PublishAsync("token_delta")
    → PgEventBus (pg_notify, SEM persistência) → AgUiSseHandler
    → AgUiEventMapper → TEXT_MESSAGE_CONTENT
```

Tokens de texto passam pelo event loop do framework e são publicados no Event Bus. **Não são persistidos** no banco (alto volume, baixo valor individual). São transmitidos via `pg_notify` com payload completo.

#### Caminho B: Tool Call Args (via Token Channel — bridge direto)

```
LLM → TokenTrackingChatClient → IAgUiTokenSink.WriteToolCallArgs()
    → AgUiTokenChannel (Channel<AgUiEvent> in-memory)
    → AgUiStreamMerger → AgUiSseHandler → TOOL_CALL_ARGS
```

Argumentos de tool call são interceptados **diretamente pelo bridge middleware** e escritos no canal in-memory, **sem passar pelo Event Bus**. Isso garante latência mínima para streaming de argumentos de tool.

### Por que dois caminhos?

| Aspecto | Caminho A (Event Bus) | Caminho B (Token Channel) |
|---------|----------------------|--------------------------|
| **O que transporta** | Tokens de texto (TextContent) | Argumentos de tool (FunctionCallContent) |
| **Origem** | Framework event loop | TokenTrackingChatClient (bridge direto) |
| **Persistência** | Não (pg_notify only) | Não (in-memory only) |
| **Latência** | Baixa (pg_notify) | Mínima (canal direto) |
| **Replay em reconexão** | Não (tokens não persistidos) | Não (canal in-memory) |
| **Recuperável** | Output final persistido | Output final persistido |

### Back-Pressure

```
src/EfsAiHub.Host.Api/Chat/AgUi/Streaming/AgUiTokenChannel.cs
```

- Canal bounded com capacidade 1000
- `BoundedChannelFullMode.DropOldest` — descarta tokens antigos se o consumidor SSE estiver lento
- Perda de tokens intermediários é aceitável: o output final completo é persistido

### Cleanup

`AgUiTokenChannelCleanupService` (HostedService):
- Executa a cada 5 minutos
- Remove canais órfãos com mais de 30 minutos de idade

---

## 10. Tool Calls e Tool Results

### Ciclo de Vida de uma Tool Call

```
← TOOL_CALL_START    { toolCallId, toolCallName, parentMessageId }
← TOOL_CALL_ARGS     { toolCallId, toolCallName, delta: "{ \"ticker\":" }
← TOOL_CALL_ARGS     { toolCallId, toolCallName, delta: "\"PETR4\" }" }
← TOOL_CALL_END      { toolCallId }
[tool executa no servidor]
← TOOL_CALL_RESULT   { toolCallId, result: "...", messageId, role: "tool" }
```

### Streaming de Argumentos

Durante o streaming do LLM, chunks de `FunctionCallContent` chegam progressivamente:
1. `TokenTrackingChatClient` detecta `FunctionCallContent`
2. Chama `IAgUiTokenSink.WriteToolCallArgs(executionId, toolCallId, toolName, argsChunk)`
3. `AgUiTokenChannel` escreve `TOOL_CALL_ARGS` no canal in-memory
4. Merge com Event Bus e entrega via SSE

### TOOL_CALL_END vs TOOL_CALL_RESULT

- `TOOL_CALL_END` — indica que a chamada ao LLM terminou de gerar os argumentos (sem campo `result`)
- `TOOL_CALL_RESULT` — resultado da execução real da tool (com campo `result`)

---

## 11. Estado Compartilhado (Shared State)

### Arquitetura de Cache (Two-Tier)

```
src/EfsAiHub.Host.Api/Chat/AgUi/State/AgUiStateManager.cs
```

```
┌─────────────────┐     ┌─────────────────┐
│ L1: MemoryCache  │ ──→ │ L2: Redis        │
│ Sliding: 30 min  │     │ TTL: 2 horas     │
│ Pod-local         │     │ Cross-pod        │
│ Key: agui:state: │     │ Key: efs:agui:   │
│     {threadId}    │     │   state:{threadId}│
└─────────────────┘     └─────────────────┘
```

| Camada | Provider | TTL | Escopo |
|--------|----------|-----|--------|
| L1 | `IMemoryCache` | 30min sliding | Pod local |
| L2 | `RedisAgUiStateStore` | 2h fixo | Cross-pod |

### Hard Cap: 32 KB

O estado compartilhado tem um limite rígido de 32 KB por conversa. Tentativas de ultrapassar o limite geram `InvalidOperationException`.

### AgUiSharedState (In-Memory)

```
src/EfsAiHub.Host.Api/Chat/AgUi/State/AgUiSharedState.cs
```

Thread-safe via `lock`:
- `GetSnapshot()` — clone do estado completo (previne mutação externa)
- `ApplyDelta(patch)` — aplica JSON Patch RFC 6902
- `SetValue(path, value)` — seta valor e gera diff

### Fluxo de Atualização

```
Agente chama StructuredOutputStateChatClient (middleware pós-resposta)
    ↓
IAgUiSharedStateWriter.UpdateAsync(threadId, path, value)
    ↓
AgUiStateManager.SetAgentValueAsync()
    ├─ Aplica mutação no L1 (AgUiSharedState)
    ├─ Gera JSON Patch (diff) via JsonPatchApplier.GenerateDiff()
    ├─ Valida tamanho ≤ 32 KB
    ├─ Salva snapshot no L2 (Redis)
    └─ Retorna AgUiEvent { Type: STATE_DELTA, Delta: patch }
```

### JSON Patch (RFC 6902)

```
src/EfsAiHub.Host.Api/Chat/AgUi/State/JsonPatchApplier.cs
```

Operações suportadas: `add`, `replace`, `remove`.

Exemplo de delta:
```json
[
  { "op": "add", "path": "/agents/coletor-boleta", "value": { "ticker": "PETR4" } },
  { "op": "replace", "path": "/agents/validador/status", "value": "approved" }
]
```

### Exemplo de Estado

```json
{
  "agents": {
    "coletor-boleta": { "ticker": "PETR4", "qty": 500, "price": 28.50 },
    "validador": { "status": "approved", "checkedAt": "2026-04-21T10:30:00Z" }
  }
}
```

Cada agente controla seu namespace dentro de `/agents/{agentId}/`.

---

## 12. Predictive State

```
src/EfsAiHub.Host.Api/Chat/AgUi/State/PredictiveStateEmitter.cs
```

Permite atualizar o estado **antes** da tool completar, usando os argumentos parciais em streaming.

### Configuração

Enviado no request `AgUiRunInput`:

```json
{
  "predictiveState": {
    "toolNameToStateField": {
      "get_portfolio": "/agents/portfolio-agent/selected",
      "fetch_cotacao": "/agents/cotacao/data"
    }
  }
}
```

### Fluxo

1. `TOOL_CALL_ARGS` chega com `toolName` e `delta` (args parciais)
2. `PredictiveStateEmitter.EmitIfMappedAsync()` verifica se `toolName` está no mapeamento
3. Extrai valor parcial do delta
4. Atualiza `AgUiStateManager.SetAgentValueAsync(threadId, statePath, partialValue)`
5. Emite `STATE_DELTA` imediatamente

### Benefício

O frontend exibe atualizações otimistas enquanto o usuário digita argumentos da tool — sem esperar a execução completa.

---

## 13. HITL via AG-UI

### Como HITL Aparece no Stream

Quando um nó com HITL é atingido, o workflow emite `hitl_required`. O `AgUiEventMapper` converte para uma sequência de tool call:

```
← TOOL_CALL_START  { toolCallId: "hitl-abc", toolCallName: "request_approval" }
← TOOL_CALL_ARGS   { toolCallId: "hitl-abc", delta: JSON(args) }
← TOOL_CALL_END    { toolCallId: "hitl-abc" }
```

O payload de args contém:
```json
{
  "interactionId": "hitl-abc",
  "question": "Aprovar envio da boleta para PETR4?",
  "options": ["Aprovar", "Rejeitar"],
  "timeoutSeconds": 600,
  "interactionType": "Approval"
}
```

### Duas Formas de Resolver

#### Forma 1: Inline (próximo POST /stream)

O frontend envia a resolução como mensagem `role="tool"`:

```json
POST /api/chat/ag-ui/stream
{
  "threadId": "thread-xyz",
  "messages": [
    { "role": "tool", "content": "approved", "toolCallId": "hitl-abc" }
  ]
}
```

O `AgUiApprovalMiddleware` intercepta antes do processamento:

```
src/EfsAiHub.Host.Api/Chat/AgUi/Approval/AgUiApprovalMiddleware.cs
```

1. Itera mensagens com `role="tool"`
2. Classifica resposta via `HitlResolutionClassifier.IsApproved(content)`
3. Chama `IHumanInteractionService.Resolve(toolCallId, content, approved)`
4. TaskCompletionSource é completado → workflow retoma

#### Forma 2: Endpoint dedicado

```json
POST /api/chat/ag-ui/resolve-hitl
{
  "toolCallId": "hitl-abc",
  "response": "approved"
}
```

Mesmo fluxo interno, sem abrir novo stream SSE.

### HitlResolutionClassifier

```
src/EfsAiHub.Platform.Runtime/Hitl/HitlResolutionClassifier.cs
```

Classifica a resposta humana:
- **JSON:** `{"approved": true}` → parse direto
- **String:** Match contra termos de rejeição: "rejected", "rejeitar", "cancelar", "no", "timeout" (case-insensitive)
- **Default:** Se nenhum termo de rejeição → aprovado

### Após Resolução

```
← TOOL_CALL_RESULT  { toolCallId: "hitl-abc", result: "approved" }
← STEP_FINISHED     (nó HITL completou)
← STEP_STARTED      (próximo nó inicia)
...
```

Se rejeitado → `HitlRejectedException` → `RUN_ERROR` com `errorCode: "HITL_REJECTED"`.

---

## 14. Reconexão e Replay

```
src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiReconnectionHandler.cs
```

### GET /reconnect/{executionId}

Headers relevantes:
- `Last-Event-ID` — último `SequenceId` recebido (enviado automaticamente pelo browser)
- `x-thread-id` — ID da conversa

### Duas Estratégias

#### Replay Parcial (com Last-Event-ID)

```
Cliente reconecta com Last-Event-ID: 12345
    ↓
1. Cancela grace period timer
2. GetHistoryAsync() do PgEventBus
3. Filtra eventos com SequenceId > 12345
4. Escreve eventos perdidos com sequence IDs originais
5. Retoma streaming normal
```

#### Resync Completo (sem Last-Event-ID)

```
Cliente reconecta sem Last-Event-ID
    ↓
1. Cancela grace period timer
2. MESSAGES_SNAPSHOT — últimas 50 mensagens do banco
3. STATE_SNAPSHOT — estado compartilhado completo
4. Retoma streaming normal
```

### Cross-Pod

Se o cliente reconecta em um pod diferente:
1. L1 cache miss → carrega estado do Redis (L2)
2. Replay/resync funciona normalmente (eventos persistidos no PostgreSQL)
3. Token channel é recriado (tokens não persistidos são perdidos — output final é persistido)

### Reconnect Protocol no Frontend

O `ChatWindowPage.handleSend` (`frontend/src/features/chat/ChatWindowPage.tsx`) implementa reconexão automática quando a conexão SSE cai durante streaming ativo. Utility em `frontend/src/features/chat/sseReconnect.ts` encapsula backoff e parsing.

**Fluxo:**

1. **Conexão inicial:** `POST /api/chat/ag-ui/stream` com body `{messages, threadId}`. Stream começa.
2. **Captura de estado durante parsing:**
   - Cada frame com `id: N` atualiza `lastEventIdRef.current`.
   - Evento `RUN_STARTED` grava `runIdRef.current = evt.runId`.
3. **Detecção de queda:** exception no `reader.read()` (rede, backend reset, timeout). Se `done=false` E `runIdRef.current` presente E erro retriável → tenta reconectar.
4. **Backoff exponencial + jitter** (paridade com C4 backend — `ResiliencePolicy.JitterRatio`):
   - `computeBackoffDelay(attempt)` — `initialDelayMs * multiplier^attempt`, cap `30_000ms`, jitter ±10%.
   - Defaults em `DEFAULT_RECONNECT_POLICY` (500ms / 2.0 / 30s cap / 0.1 jitter / 5 max attempts).
5. **Reconexão:** `GET /api/chat/ag-ui/reconnect/{runId}` com headers:
   - `Last-Event-ID: {last captured id}` → backend aplica **replay parcial** via `AgUiReconnectionHandler`.
   - `x-thread-id: {threadId}`.
6. **UI:** `SseHealthIndicator` exibe `Reconectando (N)` em âmbar durante retry; `Streaming` verde ao reestabelecer.
7. **Max attempts excedido:** propaga exception para `catch` externo → exibe bubble de erro + marca `sseStatus='error'`.

**Contrato para novas mudanças:**
- Eventos SSE devem emitir `id: N` por frame (qualquer valor monotônico). Sem isso, reconnect fará resync completo em vez de replay parcial (mais caro mas funciona).
- `RUN_STARTED` DEVE vir **antes** de qualquer outro evento de estado — é o único ponto que o frontend tem para capturar `runId`. Reconnect sem `runId` não é tentado.
- Erros 4xx (`isRetriableStreamError` retorna false) não disparam reconnect — propaga para o usuário.

**Smoke test:** durante streaming ativo, `docker compose restart backend`. UI deve mostrar `SSE: Reconectando (1)`, então (2), (3) até reconectar ou esgotar 5 tentativas em ~6s. Após reestabelecer, eventos pós-last-event-id chegam via replay.

---

## 15. Cancelamento

```
src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiCancellationHandler.cs
```

### POST /cancel

```
CancelAsync(executionId)
    ├─ Tentativa local: IExecutionSlotRegistry.TryCancel(executionId)
    │   └─ Encontra CancellationTokenSource → .Cancel()
    │       → CTS propagado para todo o workflow executor
    │
    └─ Fallback cross-pod: ICrossNodeBus.PublishCancelAsync()
        → PostgreSQL LISTEN/NOTIFY (canal: efs_exec_cancel)
        → CrossNodeCoordinator no pod correto cancela localmente
```

### Resultado

Workflow recebe cancellation token → emite `workflow_cancelled` → mapeado para `RUN_ERROR`:

```json
{
  "type": "RUN_ERROR",
  "error": "Execution cancelled",
  "errorCode": "CANCELLED",
  "timestamp": "2026-04-21T10:30:00Z"
}
```

---

## 16. Grace Period de Desconexão

```
src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiDisconnectRegistry.cs
```

### Fluxo

```
SSE desconecta (OperationCanceledException)
    ↓
ScheduleGracePeriodIfNeeded(executionId)
    ├─ HITL pendente? → NÃO agenda (timeout HITL governa)
    └─ Sem HITL → Schedule(executionId)
        ├─ Cria CancellationTokenSource
        ├─ Task.Delay(gracePeriod) → aguarda reconexão
        │
        ├─ [Se reconectar] → Cancel() chamado → timer cancelado
        │
        └─ [Se expirar] → CancelAsync(executionId)
            → Workflow cancelado → RUN_ERROR emitido
```

### Configuração

`WorkflowEngineOptions.DisconnectGracePeriodSeconds` — default: 120 segundos.

---

## 17. Deduplicação (SequenceId)

### Origem

- **Eventos persistidos** (lifecycle, steps, HITL, state): `SequenceId` = PK auto-incremento da tabela `workflow_event_audit`
- **Tokens** (não persistidos): `SequenceId = 0`

### Nível 1: PgEventBus.SubscribeAsync

```csharp
HashSet<long> seen = new();

// Fase replay: todos os eventos persistidos
foreach (var evt in GetAllAsync())
    seen.Add(evt.SequenceId);
    yield return evt;

// Fase live: LISTEN notifications
foreach (var evt in liveChannel)
    if (evt.SequenceId > 0 && seen.Contains(evt.SequenceId))
        continue;  // Duplicata → skip
    seen.Add(evt.SequenceId);
    yield return evt;
```

### Nível 2: AgUiSseHandler

```csharp
long lastSequenceId = 0;

await foreach (var envelope in eventBus.SubscribeAsync())
    if (envelope.SequenceId > 0 && envelope.SequenceId <= lastSequenceId)
        continue;  // Já entregue → skip
    if (envelope.SequenceId > 0)
        lastSequenceId = envelope.SequenceId;
    // ...emit
```

### Nível 3: Reconexão

Cliente envia `Last-Event-ID` → servidor filtra `SequenceId > lastEventId` → replay sem duplicatas.

**Por que SequenceId e não Timestamp:** PK auto-incremento é determinístico; timestamps podem colidir sob alta frequência.

---

## 18. Merge de Streams

```
src/EfsAiHub.Host.Api/Chat/AgUi/Streaming/AgUiStreamMerger.cs
```

### Arquitetura

```
┌─────────────────┐    ┌─────────────────┐
│ PgEventBus       │    │ AgUiTokenChannel │
│ (persistido)     │    │ (in-memory)      │
│ lifecycle, HITL, │    │ tokens, tool args│
│ state, steps     │    │                  │
└────────┬────────┘    └────────┬────────┘
         │                      │
         ▼                      ▼
    ┌─────────────────────────────────┐
    │ Bounded Channel (cap: 500)       │
    │ BoundedChannelFullMode.DropOldest│
    └────────────────┬────────────────┘
                     │
                     ▼
            AgUiSseHandler
              (escritor SSE)
```

### Dois Producers em Paralelo

1. **EventBus producer:** consome `IWorkflowEventBus.SubscribeAsync()` → mapeia para AG-UI events → escreve no merged channel
2. **Token producer:** consome `AgUiTokenChannel` → escreve no merged channel

### Back-Pressure

Canal merged com capacidade 500, `DropOldest`:
- Previne OOM se o consumidor SSE estiver lento (rede lenta)
- Tokens intermediários perdidos são recuperáveis via output final persistido
- Coordenação via `Task.WhenAll()` dos producers

---

## 19. Frontend Tools

```
src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiFrontendToolHandler.cs
```

O cliente pode declarar tools que existem no frontend:

### Request

```json
{
  "tools": [
    {
      "name": "show_chart",
      "description": "Exibe gráfico no frontend",
      "parameters": { "type": "object", "properties": { "data": { "type": "array" } } }
    }
  ]
}
```

### Registro

`AgUiFrontendToolHandler.RegisterFrontendTools()`:
1. Prefixa nome com `frontend_` (ex: `frontend_show_chart`)
2. Cria stubs que retornam `_frontendAction: true` no resultado
3. Disponibiliza para o agente como tool invocável

### Resultado

Quando o agente invoca uma frontend tool:
```json
← TOOL_CALL_START  { toolCallName: "frontend_show_chart" }
← TOOL_CALL_ARGS   { delta: "{ \"data\": [...] }" }
← TOOL_CALL_END
← TOOL_CALL_RESULT { result: "{ \"_frontendAction\": true, \"tool\": \"show_chart\", \"args\": {...} }" }
```

O frontend interpreta `_frontendAction: true` e executa a ação localmente.

---

## 20. Rate Limiting

### Por Usuário e Conversa

```
src/EfsAiHub.Host.Api/Chat/Services/ChatRateLimiter.cs
```

Redis Sorted Set com sliding window (Lua script atômico):

| Limite | Default | Chave Redis |
|--------|---------|-------------|
| Per-user | 10 msgs / 60s | `rl:chat:{userId}` |
| Per-conversation | 5 msgs / 60s | `rl:chat:conv:{conversationId}` |

### Por Projeto

```
src/EfsAiHub.Host.Api/Chat/Services/ProjectRateLimiter.cs
```

| Limite | Default | Chave Redis |
|--------|---------|-------------|
| Per-project | Configurável | `rl:project:{projectId}` |

### Lua Script (Sliding Window)

```lua
ZREMRANGEBYSCORE key -inf (now - window)
ZCARD key
IF count < maxCount THEN
    ZADD key now member
    PEXPIRE key windowMs
    RETURN 1  -- allowed
ELSE
    RETURN 0  -- rate limited
END
```

### Fail-Open

Se Redis estiver indisponível, loga warning e permite a requisição (fail-open).

### Resposta

Rate limit excedido → `ConversationOperationStatus.RateLimited` → HTTP 429.

### Decisão de Design — Sharding de Keys

O rate limiter usa uma key Redis por userId (`rl:chat:{userId}`). Sharding de keys (ex: `rl:chat:{userId}:{shard}`) **não é necessário** neste contexto porque:
- O caso de uso é chat (1 mensagem por vez por humano) — volume máximo por userId é baixo
- O Lua script é atômico e executa em microsegundos (ZADD + ZCARD)
- Hot keys só seriam problema com >10K req/s para um único userId, cenário impossível em chat interativo
- A sliding window com PEXPIRE garante cleanup automático

### Endpoints Anônimos — Rate Limit per-IP

Os endpoints AG-UI usam `.AllowAnonymous()` para permitir acesso sem headers de identidade (ex: demos, chatbots públicos). Para evitar que todos os anônimos compartilhem um único bucket de rate limit (`"anonymous"`), o sistema discrimina por IP:

- Quando `userId` resolve para `"anonymous"`, o discriminador passa a ser `"anon:{IP}"` (ex: `"anon:192.168.1.1"`)
- Cada IP anônimo tem seu próprio sliding window no Redis
- Requests autenticados (com headers `x-efs-account` ou JWT) continuam usando o userId real

---

## 21. Concorrência por Conversa

```
src/EfsAiHub.Host.Api/Chat/Services/ConversationLockManager.cs
```

### Problema

Múltiplas requisições `/stream` para a mesma conversa poderiam disparar workflows duplicados.

### Solução

```csharp
ConversationLockManager
{
    _locks: ConcurrentDictionary<conversationId, LockEntry>
    // LockEntry = SemaphoreSlim(1,1) com ref counting
}
```

- `AcquireAsync(conversationId)` — aguarda até 10s pelo lock
- Apenas uma requisição por conversa executa `SendMessagesAsync()` ao mesmo tempo
- Lock liberado automaticamente via `IDisposable`
- Ref counting: semáforo removido quando nenhum consumidor o referencia

---

## 22. Persistência de Mensagens

```
src/EfsAiHub.Host.Api/Chat/Services/ConversationService.Messaging.cs
```

### Mensagens do Usuário

```
POST /stream com messages[{role:"user", content:"..."}]
    ↓
ConversationService.SendMessagesAsync()
    ├─ BuildChatMessage() para cada input
    │   ├─ Gera MessageId
    │   ├─ Mapeia role ("robot" → "assistant")
    │   └─ TokenCount: 0 (atualizado depois)
    │
    └─ SaveBatchAsync() → PgChatMessageRepository
        └─ Bulk INSERT no PostgreSQL
```

### Mensagens do Assistente

```
Workflow completa → OnExecutionCompletedAsync()
    ├─ Valida: ActiveExecutionId == executionId (idempotente)
    ├─ Parseia output final → ExecutionOutputParser.Parse()
    ├─ Constrói ChatMessage com role="assistant"
    ├─ SaveAsync() → PostgreSQL
    ├─ TokenCountUpdater.EnqueueUpdate() (fire-and-forget)
    │   └─ Busca contagem real de tokens do ILlmTokenUsageRepository
    │   └─ Atualiza TokenCount na mensagem
    └─ Limpa ActiveExecutionId na sessão
```

---

## 23. Mapeamento de Eventos Internos

```
src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEventMapper.cs
```

| Evento Interno | → | Evento(s) AG-UI |
|----------------|---|-----------------|
| `workflow_started` | → | `RUN_STARTED` |
| `workflow_completed` | → | `RUN_FINISHED` |
| `workflow_failed` | → | `RUN_ERROR` |
| `workflow_cancelled` | → | `RUN_ERROR` |
| `error` | → | `RUN_ERROR` |
| `budget_exceeded` | → | `RUN_ERROR` (code: BUDGET_EXCEEDED) |
| `step_started` / `node_started` | → | `STEP_STARTED` |
| `step_completed` / `node_completed` | → | `STEP_FINISHED` + `TEXT_MESSAGE_*` (se output não streamed) |
| `text_message_start` | → | `TEXT_MESSAGE_START` |
| `token` / `token_delta` | → | `TEXT_MESSAGE_CONTENT` |
| `text_message_end` | → | `TEXT_MESSAGE_END` |
| `tool_call_started` | → | `TOOL_CALL_START` |
| `tool_call_args` | → | `TOOL_CALL_ARGS` |
| `tool_call_completed` | → | `TOOL_CALL_END` |
| `hitl_required` | → | `TOOL_CALL_START` + `TOOL_CALL_ARGS` + `TOOL_CALL_END` (toolName: request_approval) |
| `hitl_resolved` | → | `TOOL_CALL_RESULT` |
| `state_snapshot` | → | `STATE_SNAPSHOT` |
| `state_delta` | → | `STATE_DELTA` |
| `escalation_requested` | → | `CUSTOM` (name: ESCALATION) |
| (desconhecido) | → | [] (lista vazia — forward-compatible) |

### Lógica Especial: step_completed com Output

Se `step_completed` contém `output` e `wasStreamed=false`:
1. Emite `STEP_FINISHED`
2. Emite `TEXT_MESSAGE_START` (messageId, role="assistant")
3. Emite `TEXT_MESSAGE_CONTENT` (delta = output inteiro)
4. Emite `TEXT_MESSAGE_END` (messageId)

Isso garante que outputs de nós que não fizeram streaming (ex: executores de código) sejam entregues como mensagens de texto.

---

## 24. Pipeline de Middleware HTTP

```
src/EfsAiHub.Host.Api/Program.cs
```

```
HttpRequest
    ↓
SecurityHeadersMiddleware          — headers de segurança
    ↓
TenantMiddleware                   — resolve tenant do header
    ↓
ProjectMiddleware                  — resolve projeto do header
    ↓
DefaultProjectGuard                — garante projeto válido
    ↓
ProjectRateLimitMiddleware         — rate limit por projeto (429)
    │                                + budget guard (402)
    │                                Redis sliding window
    │                                Fail-open se Redis indisponível
    ↓
Authorization                      — autenticação/autorização
    ↓
AdminGateMiddleware                — gate administrativo
    ↓
🎯 MapAgUiEndpoints()             — rotas AG-UI
```

---

## 25. Configuração

### WorkflowEngineOptions (AG-UI Relevante)

```
src/EfsAiHub.Platform.Runtime/Options/WorkflowEngineOptions.cs
```

| Propriedade | Default | Descrição |
|-------------|---------|-----------|
| `DisconnectGracePeriodSeconds` | 120 | Grace period antes de auto-cancel após desconexão SSE |
| `ChatMaxConcurrentExecutions` | 200 | Back-pressure global para Chat path |

### ChatRateLimitOptions

| Propriedade | Default | Descrição |
|-------------|---------|-----------|
| `MaxMessages` | 10 | Limite per-user por janela |
| `WindowSeconds` | 60 | Janela de tempo (sliding window) |
| `MaxMessagesPerConversation` | 5 | Limite per-conversation por janela |
| `ConversationWindowSeconds` | 60 | Janela per-conversation |

### DI Registration

```csharp
// Singletons AG-UI
builder.Services.AddSingleton<AgUiEventMapper>();
builder.Services.AddSingleton<AgUiTokenChannel>();
builder.Services.AddSingleton<IAgUiTokenSink>(sp => sp.GetRequiredService<AgUiTokenChannel>());
builder.Services.AddSingleton<IAgUiStateStore, RedisAgUiStateStore>();
builder.Services.AddSingleton<AgUiStateManager>();
builder.Services.AddSingleton<AgUiSharedStateWriterAdapter>();
builder.Services.AddSingleton<IAgUiSharedStateWriter>(...);
builder.Services.AddSingleton<PredictiveStateEmitter>();
builder.Services.AddSingleton<AgUiDisconnectRegistry>();
builder.Services.AddSingleton<AgUiSseHandler>();
builder.Services.AddSingleton<AgUiApprovalMiddleware>();
builder.Services.AddSingleton<AgUiCancellationHandler>();
builder.Services.AddSingleton<AgUiFrontendToolHandler>();
builder.Services.AddSingleton<AgUiReconnectionHandler>();

// Hosted services
builder.Services.AddHostedService<AgUiTokenChannelCleanupService>();

// Cache
builder.Services.AddMemoryCache();
```

Todos os componentes AG-UI são **singletons** (compartilhados entre requests).

---

## 26. Tratamento de Erros

### Nível 1: Endpoint (antes do SSE)

Erros antes de abrir o stream SSE retornam JSON:

| Situação | Status | Corpo |
|----------|--------|-------|
| Sem mensagem de usuário | 400 | `{ "error": "No user message provided" }` |
| Conversa não encontrada | 404 | `{ "error": "Conversation not found" }` |
| Rate limit excedido | 429 | `{ "error": "Rate limit exceeded" }` |
| Budget de projeto esgotado | 402 | `{ "error": "Project budget exceeded" }` |

### Nível 2: Stream SSE (durante execução)

Erros durante a execução são entregues como eventos:

```json
{
  "type": "RUN_ERROR",
  "error": "Budget exceeded: 50123 tokens used, limit 50000",
  "errorCode": "BUDGET_EXCEEDED",
  "timestamp": "2026-04-21T10:30:00Z"
}
```

| errorCode | Origem |
|-----------|--------|
| `TIMEOUT` | Timeout da execução |
| `CANCELLED` | Cancelamento manual |
| `BUDGET_EXCEEDED` | Token ou custo excedido |
| `HITL_REJECTED` | Humano rejeitou |
| `LLM_ERROR` | Erro do provider LLM |
| `LLM_RATE_LIMIT` | Rate limit do provider |
| `LLM_CONTENT_FILTER` | Filtro de conteúdo |
| `TOOL_ERROR` | Erro em tool |
| `FRAMEWORK_ERROR` | Erro do framework |
| `BACK_PRESSURE_REJECTED` | Rejeitado por back-pressure |
| `AGENT_LOOP_LIMIT` | Excedeu MaxAgentInvocations |

### Nível 3: Desconexão

- `OperationCanceledException` → grace period agendado (ou não, se HITL pendente)
- Resposta SSE encerrada graciosamente via `response.CompleteAsync()`
- Canais limpos no finally block

### Sem Recovery Parcial

Uma vez emitido `RUN_ERROR`, o stream SSE encerra. Não há mecanismo de retry automático no nível do protocolo — o cliente deve iniciar nova requisição.

---

## Referência Rápida de Arquivos

| Componente | Arquivo |
|-----------|---------|
| Endpoints | `src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEndpoints.cs` |
| Event Model | `src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiEvent.cs` |
| Event Mapper | `src/EfsAiHub.Host.Api/Chat/AgUi/AgUiEventMapper.cs` |
| SSE Handler | `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiSseHandler.cs` |
| Reconnection | `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiReconnectionHandler.cs` |
| Cancellation | `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiCancellationHandler.cs` |
| Disconnect | `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiDisconnectRegistry.cs` |
| Frontend Tools | `src/EfsAiHub.Host.Api/Chat/AgUi/Handlers/AgUiFrontendToolHandler.cs` |
| Approval | `src/EfsAiHub.Host.Api/Chat/AgUi/Approval/AgUiApprovalMiddleware.cs` |
| Stream Merger | `src/EfsAiHub.Host.Api/Chat/AgUi/Streaming/AgUiStreamMerger.cs` |
| Token Channel | `src/EfsAiHub.Host.Api/Chat/AgUi/Streaming/AgUiTokenChannel.cs` |
| Channel Cleanup | `src/EfsAiHub.Host.Api/Chat/AgUi/Streaming/AgUiTokenChannelCleanupService.cs` |
| State Manager | `src/EfsAiHub.Host.Api/Chat/AgUi/State/AgUiStateManager.cs` |
| Shared State | `src/EfsAiHub.Host.Api/Chat/AgUi/State/AgUiSharedState.cs` |
| Redis Store | `src/EfsAiHub.Host.Api/Chat/AgUi/State/RedisAgUiStateStore.cs` |
| State Writer | `src/EfsAiHub.Host.Api/Chat/AgUi/State/AgUiSharedStateWriterAdapter.cs` |
| JSON Patch | `src/EfsAiHub.Host.Api/Chat/AgUi/State/JsonPatchApplier.cs` |
| Predictive State | `src/EfsAiHub.Host.Api/Chat/AgUi/State/PredictiveStateEmitter.cs` |
| Run Input | `src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiRunInput.cs` |
| Input Message | `src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiInputMessage.cs` |
| Frontend Tool | `src/EfsAiHub.Host.Api/Chat/AgUi/Models/AgUiFrontendTool.cs` |
| IAgUiTokenSink | `src/EfsAiHub.Core.Abstractions/AgUi/IAgUiTokenSink.cs` |
| IAgUiSharedStateWriter | `src/EfsAiHub.Core.Abstractions/AgUi/IAgUiSharedStateWriter.cs` |
| Event Bus | `src/EfsAiHub.Core.Orchestration/Workflows/IWorkflowEventBus.cs` |
| PgEventBus | `src/EfsAiHub.Infra.Messaging/PgEventBus.cs` |
| Conversation Facade | `src/EfsAiHub.Host.Api/Chat/Services/ConversationFacade.cs` |
| Conversation Service | `src/EfsAiHub.Host.Api/Chat/Services/ConversationService.cs` |
| Chat Rate Limiter | `src/EfsAiHub.Host.Api/Chat/Services/ChatRateLimiter.cs` |
| Lock Manager | `src/EfsAiHub.Host.Api/Chat/Services/ConversationLockManager.cs` |
| HITL Classifier | `src/EfsAiHub.Platform.Runtime/Hitl/HitlResolutionClassifier.cs` |
