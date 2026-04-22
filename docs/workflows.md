# Workflows — Guia Completo

> O que é um workflow, o que ele tem, como criar um workflow e como ele funciona no EfsAiHub.

---

## Sumário

1. [Contrato do Workflow](#1-contrato-do-workflow)
2. [Propriedades e Configuração](#2-propriedades-e-configuração)
3. [Modos de Orquestração](#3-modos-de-orquestração)
4. [Nós: Agentes e Executores](#4-nós-agentes-e-executores)
5. [Edges (Arestas do Grafo)](#5-edges-arestas-do-grafo)
6. [Ciclo de Vida da Execução](#6-ciclo-de-vida-da-execução)
7. [Modelo de Dispatch](#7-modelo-de-dispatch)
8. [Estado e Rastreamento por Nó](#8-estado-e-rastreamento-por-nó)
9. [Human-in-the-Loop (HITL)](#9-human-in-the-loop-hitl)
10. [Escalation e Roteamento](#10-escalation-e-roteamento)
11. [Versionamento de Workflow](#11-versionamento-de-workflow)
12. [Sandbox](#12-sandbox)
13. [Budget e Controle de Custo](#13-budget-e-controle-de-custo)
14. [Middleware por Agente](#14-middleware-por-agente)
15. [Providers por Nó](#15-providers-por-nó)
16. [Event Bus e Auditoria](#16-event-bus-e-auditoria)
17. [AG-UI e Streaming SSE](#17-ag-ui-e-streaming-sse)
18. [Rate Limiting e Concorrência](#18-rate-limiting-e-concorrência)
19. [Enrichment e Disclaimers](#19-enrichment-e-disclaimers)
20. [Checkpoint e Recovery](#20-checkpoint-e-recovery)
21. [Observabilidade](#21-observabilidade)
22. [Categorias de Erro](#22-categorias-de-erro)
23. [API REST](#23-api-rest)
24. [Persistência](#24-persistência)
25. [Como Criar um Workflow](#25-como-criar-um-workflow)
26. [Workflows Cadastrados](#26-workflows-cadastrados)

---

## 1. Contrato do Workflow

O workflow é a unidade de orquestração do EfsAiHub. Ele compõe múltiplos agentes e/ou executores de código em um fluxo controlado, com budget compartilhado, HITL declarativo, auditoria e coordenação cross-pod.

### Modelo de Domínio

```
src/EfsAiHub.Core.Orchestration/Workflows/WorkflowDefinition.cs
```

```csharp
public class WorkflowDefinition
{
    public string ProjectId { get; set; } = "default";
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Version { get; init; } = "1.0.0";
    public required OrchestrationMode OrchestrationMode { get; init; }
    public required IReadOnlyList<WorkflowAgentReference> Agents { get; init; }
    public IReadOnlyList<WorkflowExecutorStep> Executors { get; init; } = [];
    public IReadOnlyList<WorkflowEdge> Edges { get; init; } = [];
    public IReadOnlyList<RoutingRule> RoutingRules { get; init; } = [];
    public WorkflowConfiguration Configuration { get; init; } = new();
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public string Visibility { get; init; } = "project";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// Factory validante — lança DomainException se invariantes forem violadas.
    public static WorkflowDefinition Create(/* parâmetros nomeados */ );
    /// Revalidação explícita — idempotente; callers de persistência chamam após deserialize.
    public void EnsureInvariants();
}
```

### Princípios

| Princípio | Como se aplica |
|-----------|---------------|
| **Isolamento por projeto** | Todo workflow tem `ProjectId`; query filters no DbContext isolam por projeto |
| **Composabilidade** | 5 modos de orquestração configuráveis sem código |
| **Declarativo** | HITL, budget, routing, enrichment — tudo via JSON, não deploy |
| **Versionamento** | Append-only snapshots com ContentHash SHA-256 |
| **Invariantes protegidas** | `Create()` valida no construtor; deserialize pode chamar `EnsureInvariants()` |

### Construção validada

Duas entradas para construir um `WorkflowDefinition`:

1. **Código imperativo** (controllers, application services, clones) — use `WorkflowDefinition.Create(...)`. O factory lança `DomainException` (mapeada para `HTTP 400`) se alguma regra de negócio for violada.
2. **Deserialização JSON** (repositórios lendo do Postgres) — use `JsonSerializer.Deserialize<WorkflowDefinition>(json, JsonDefaults.Domain)` normalmente. Chame `EnsureInvariants()` explicitamente após o mapeamento se quiser revalidar.

**Invariantes atualmente protegidas:**

- `Id` e `Name` não podem ser vazios.
- Modos `Sequential`, `Concurrent`, `Handoff`, `GroupChat` exigem `Agents.Count >= 1`.
- Modo `Graph` exige `Edges.Count >= 1`.
- Em modo `Graph`, cada `Edge.From`/`Edge.To` deve referenciar um `AgentId` ou `Executor.Id` existente na definição.

Analogamente:
- `Project.Create(...)` valida `Id`/`TenantId`/`Name` não-vazios + `Budget` não-negativo.
- `AgentDefinition.Create(...)` valida `Id`/`Name`/`Model.DeploymentName` não-vazios + `Temperature` em `[0, 2]` quando presente.

---

## 2. Propriedades e Configuração

### WorkflowConfiguration

```
src/EfsAiHub.Core.Orchestration/Workflows/WorkflowDefinition.cs (nested)
```

| Propriedade | Tipo | Default | Descrição |
|-------------|------|---------|-----------|
| `MaxRounds` | int? | null | Limite de rodadas (GroupChat) |
| `TimeoutSeconds` | int | 300 | Timeout global da execução |
| `EnableHumanInTheLoop` | bool | false | Habilita HITL no workflow |
| `CheckpointMode` | string | "InMemory" | "InMemory" · "Postgres" · "Blob" |
| `ExposeAsAgent` | bool | false | Expõe o workflow como agente aninhado |
| `ExposedAgentDescription` | string? | null | Descrição quando exposto como agente |
| `InputMode` | string | "Standalone" | "Standalone" (single-shot) · "Chat" (conversacional) |
| `MaxHistoryMessages` | int | 20 | Janela de histórico no modo Chat |
| `MaxHistoryTokens` | int? | null | Budget de tokens para histórico (null = sem limite) |
| `MaxAgentInvocations` | int | 10 | Proteção contra loop de handoff |
| `MaxTokensPerExecution` | int | 50000 | Hard cap de tokens LLM por execução |
| `MaxCostUsdPerExecution` | decimal? | null | Hard cap de custo em USD (null = sem enforcement) |
| `OutputNodes` | List\<string\>? | null | Nós terminais explícitos (Graph mode) |
| `MaxConcurrentExecutions` | int? | null | Semáforo por workflow (null = ilimitado) |
| `EnrichmentRules` | List\<EnrichmentRule\>? | null | Regras declarativas de enrichment |

### WorkflowEngineOptions (Configuração Global)

```
src/EfsAiHub.Platform.Runtime/Options/WorkflowEngineOptions.cs
```

| Propriedade | Default | Descrição |
|-------------|---------|-----------|
| `MaxConcurrentExecutions` | 10 | Concorrência global via `IExecutionSlotRegistry` |
| `ChatMaxConcurrentExecutions` | 200 | Back-pressure para Chat path |
| `DefaultTimeoutSeconds` | 300 | Timeout default |
| `HitlRecoveryConcurrency` | 4 | Concorrência de recovery HITL |
| `HitlRecoveryBatchSize` | 100 | Batch de recovery |
| `HitlRecoveryIntervalSeconds` | 30 | Polling interval do recovery |
| `AuditRetentionDays` | 30 | Retenção de workflow_event_audit |
| `ToolInvocationRetentionDays` | 14 | Retenção de invocações de tool |
| `CheckpointRetentionDays` | 14 | Retenção de checkpoints |
| `DisconnectGracePeriodSeconds` | 120 | Grace period em desconexão SSE |
| `AllowToolFingerprintMismatch` | true | Tolerância a mudança de fingerprint de tool |

---

## 3. Modos de Orquestração

```
src/EfsAiHub.Core.Orchestration/Enums/OrchestrationMode.cs
```

```csharp
public enum OrchestrationMode
{
    Sequential,    // Execução sequencial
    Concurrent,    // Execução paralela
    Handoff,       // Handoff entre agentes
    GroupChat,     // Chat em grupo com manager
    Graph          // Grafo dirigido com edges tipados
}
```

### 3.1 Sequential

Agentes executam na ordem declarada. A saída do agente N é a entrada do agente N+1.

```
Agente A → Agente B → Agente C → Output
```

**Builder:** `AgentWorkflowBuilder.BuildSequential(agents)`

**Quando usar:** Pipelines lineares — extração → análise → formatação.

### 3.2 Concurrent

Todos os agentes executam em paralelo. As saídas são combinadas no `WorkflowOutputEvent`.

```
         ┌→ Agente A ─┐
Input ───┤→ Agente B ──┤→ Output (combinado)
         └→ Agente C ─┘
```

**Builder:** `AgentWorkflowBuilder.BuildConcurrent(agents)`

**Quando usar:** Análises independentes que podem rodar ao mesmo tempo (ex: sentimento + compliance + resumo).

### 3.3 Handoff

Grafo dirigido com um agente manager (hub) e especialistas. O manager delega para especialistas com base no contexto.

```
         ┌→ Especialista 1 ──┐
Manager ─┤→ Especialista 2 ──┤→ Manager (decide)
         └→ Especialista 3 ──┘
```

**Builder:** `AgentWorkflowBuilder.CreateHandoffBuilderWith(entryAgent).WithHandoff(from, to, condition)`

**Topologia default:** O primeiro agente é o manager; edges bidirecionais manager↔especialista.

**Proteção contra loop:** Detecção de ping-pong (A→B→A sem output) — falha após 3 ciclos consecutivos. Também limitado por `MaxAgentInvocations`.

**Continuação:** Suporta `startAgentId` para retomar a partir do último agente ativo (otimização para Chat mode).

### 3.4 GroupChat

Chat em grupo com um manager que orquestra a conversa entre participantes.

```
Manager (orquestra)
  ├─ Participante 1
  ├─ Participante 2
  └─ Participante 3
```

**Builder:** `AgentWorkflowBuilder.CreateGroupChatBuilderWith(manager).AddParticipants(participants)`

**Roles:**
- `role: "manager"` — orquestrador (máximo 1 por workflow)
- `role: "participant"` — participantes regulares

**Terminação:** Controlada por `MaxRounds` (default 5 iterações).

### 3.5 Graph (Low-Level)

Grafo dirigido completo usando `WorkflowBuilder`. Suporta todos os tipos de edge, mistura de agentes LLM com executores de código, e topologias arbitrárias.

```
[ChatTrigger] → Executor A → Agente B ──(condition)──→ Agente C
                                     └──(default)───→ Executor D → [Output]
```

**Builder:** `WorkflowBuilder` com adição manual de nós e edges.

**Características exclusivas:**
- Mistura agentes (LLM) com `DelegateExecutor` (código puro)
- Todos os 5 tipos de edge (Direct, Conditional, Switch, FanOut, FanIn)
- `ChatTriggerExecutor` auto-injetado como nó de entrada (converte `List<ChatMessage>` → string)
- `InputSourceBridge` auto-injetado para edges com `InputSource="WorkflowInput"`
- `OutputNodes` configura nós terminais explícitos

---

## 4. Nós: Agentes e Executores

### 4.1 WorkflowAgentReference

Referência a um agente dentro do workflow.

```csharp
public class WorkflowAgentReference
{
    public required string AgentId { get; init; }
    public string? Role { get; init; }           // "manager" | "participant"
    public NodeHitlConfig? Hitl { get; init; }   // HITL declarativo por nó
}
```

Cada agente referenciado é resolvido via `IAgentFactory` e recebe seu próprio pipeline de middleware, provider e budget compartilhado.

### 4.2 WorkflowExecutorStep (Code Executor)

Nó de código puro — sem LLM. Disponível apenas no modo Graph.

```csharp
public class WorkflowExecutorStep
{
    public required string Id { get; init; }
    public required string FunctionName { get; init; }
    public string? Description { get; init; }
    public NodeHitlConfig? Hitl { get; init; }
}
```

Registrado em `ICodeExecutorRegistry`. Exemplos de executores:
- `search_single` — pesquisa web de um ativo individual
- `atendimento_pre_processor` — pre-processamento de ChatTurnContext
- `atendimento_post_processor` — validação e enrichment pós-LLM

### 4.3 DelegateExecutor

```
src/EfsAiHub.Core.Orchestration/Executors/DelegateExecutor.cs
```

Wrapper para `Func<string, CancellationToken, Task<string>>`. Armazena o `ExecutionContext` em `AsyncLocal` para que cada executor acesse budget, metadata e callbacks compartilhados.

### 4.4 ChatTriggerExecutor

```
src/EfsAiHub.Core.Orchestration/Executors/ChatTriggerExecutor.cs
```

Nó de entrada auto-injetado no modo Graph:
- **Input:** `List<ChatMessage>` (conversa até o momento)
- **Output:** string (texto do usuário extraído)
- **FixedId:** `__chat_trigger__`

---

## 5. Edges (Arestas do Grafo)

```
src/EfsAiHub.Core.Orchestration/Enums/WorkflowEdgeType.cs
```

### Tipos de Edge

```csharp
public enum WorkflowEdgeType
{
    Direct,       // 1→1 sem condição
    Conditional,  // 1→1 com condição (substring match)
    Switch,       // 1→N com cases e default
    FanOut,       // 1→N paralelo
    FanIn         // N→1 barreira (aguarda todos)
}
```

### Modelo de Edge

```csharp
public class WorkflowEdge
{
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Condition { get; init; }
    public WorkflowEdgeType EdgeType { get; init; } = WorkflowEdgeType.Direct;
    public List<string> Targets { get; init; } = [];    // FanOut: alvos paralelos
    public List<string> Sources { get; init; } = [];    // FanIn: fontes da barreira
    public List<WorkflowSwitchCase> Cases { get; init; } = [];
    public string? InputSource { get; init; }           // null=default | "WorkflowInput"
}
```

### 5.1 Direct

A→B sem condição. O output de A é o input de B.

```json
{ "from": "agente-a", "to": "agente-b", "edgeType": "Direct" }
```

### 5.2 Conditional

A→B somente se o output de A contiver a substring `condition`.

```json
{ "from": "analise", "to": "alerta", "condition": "risco alto", "edgeType": "Conditional" }
```

### 5.3 Switch

A→{B,C,D} com cases baseados em substring match. Suporta case default.

```json
{
  "from": "classificador",
  "edgeType": "Switch",
  "cases": [
    { "condition": "compra", "targets": ["executor-compra"] },
    { "condition": "venda", "targets": ["executor-venda"] },
    { "isDefault": true, "targets": ["fallback"] }
  ]
}
```

### 5.4 FanOut

A→{B,C,D} em paralelo. Todos os targets recebem o output de A e executam simultaneamente.

```json
{ "from": "distribuidor", "targets": ["analise-a", "analise-b", "analise-c"], "edgeType": "FanOut" }
```

### 5.5 FanIn

{A,B,C}→D barreira. D só executa quando todos os sources completarem.

```json
{ "to": "consolidador", "sources": ["analise-a", "analise-b", "analise-c"], "edgeType": "FanIn" }
```

### InputSource Bridge

Quando `InputSource="WorkflowInput"`, o edge injeta automaticamente um `InputSourceBridgeExecutor` que restaura o input original do workflow em vez de usar o output do nó anterior.

### Edge Handlers

```
src/EfsAiHub.Platform.Runtime/Factories/EdgeHandlers.cs
```

Strategy pattern com implementações: `DirectEdgeHandler`, `ConditionalEdgeHandler`, `SwitchEdgeHandler`, `FanOutEdgeHandler`, `FanInEdgeHandler`.

---

## 6. Ciclo de Vida da Execução

### Estados

```
src/EfsAiHub.Core.Orchestration/Enums/WorkflowStatus.cs
```

```csharp
public enum WorkflowStatus
{
    Pending,     // Aguardando execução
    Running,     // Em execução
    Paused,      // Pausado (aguardando HITL)
    Completed,   // Concluído com sucesso
    Failed,      // Falhou
    Cancelled    // Cancelado explicitamente
}
```

### Diagrama de Transição

```
Pending ──→ Running ──→ Completed
                │
                ├──→ Paused ──→ Running (HITL aprovado)
                │         └──→ Failed  (HITL rejeitado/expirado)
                │
                ├──→ Failed    (erro, timeout, budget)
                └──→ Cancelled (cancelamento manual)
```

### Modelo de Execução

```
src/EfsAiHub.Core.Orchestration/Workflows/WorkflowExecution.cs
```

```csharp
public class WorkflowExecution
{
    public required string ExecutionId { get; init; }
    public required string WorkflowId { get; init; }
    public string ProjectId { get; set; } = "default";
    public WorkflowStatus Status { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? ErrorMessage { get; set; }
    public ErrorCategory? ErrorCategory { get; set; }
    public List<ExecutionStep> Steps { get; set; } = [];
    public string? CheckpointKey { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public ConcurrentDictionary<string, string> Metadata { get; set; } = new();
}
```

### ExecutionStep (por nó)

```csharp
public class ExecutionStep
{
    public required string StepId { get; init; }
    public required string AgentId { get; init; }
    public required string AgentName { get; init; }
    public WorkflowStatus Status { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TokensUsed { get; set; }
}
```

### Metadados da Execução

O `ConcurrentDictionary<string, string> Metadata` armazena:
- `conversationId` — ID da conversa AG-UI
- `lastActiveAgentId` — último agente ativo (para continuação Handoff)
- `promptVersions` — versões de prompt por agente
- `startAgentId` — agente de entrada para continuação

---

## 7. Modelo de Dispatch

```
src/EfsAiHub.Platform.Runtime/Application/Services/WorkflowService.cs
```

O `WorkflowService.TriggerAsync()` executa todas as fontes de execução via `Task.Run()` com back-pressure controlado pelo `IExecutionSlotRegistry`.

### 7.1 Chat Path (ExecutionSource.Chat)

```
API Request → WorkflowService → Task.Run() → WorkflowExecutor → Runner
```

- **Execução direta** via `Task.Run()`
- Verifica `IExecutionSlotRegistry` para back-pressure global (máximo `ChatMaxConcurrentExecutions`)
- Registra `CancellationTokenSource` em `ChatExecutionRegistry`
- Resposta 202 imediata com `executionId`
- DI scope isolado por execução
- Otimizado para latência mínima (streaming real-time)

### 7.2 Api / Webhook / A2A Path

```
API Request → WorkflowService → Task.Run() → WorkflowExecutor → Runner
```

- **Execução direta** via `Task.Run()`
- Verifica `IExecutionSlotRegistry` para back-pressure global (máximo `MaxConcurrentExecutions`)
- **Guard de concorrência em duas camadas:**
  1. `SemaphoreSlim` local por workflow + projeto + limite
  2. `CountRunningAsync()` no banco (guard distribuído global)
- Novo `IServiceScope` por execução

### Diagrama de Dispatch

```
               ┌─ Chat ──────────→ Task.Run() (IExecutionSlotRegistry)
               │
TriggerAsync ──┤─ Api/Webhook/A2A → Task.Run() (IExecutionSlotRegistry)
```

### ExecutionSource Enum

```csharp
public enum ExecutionSource
{
    Api,       // Trigger via API admin
    Chat,      // Chat Path (AG-UI)
    Webhook,   // Trigger externo
    A2A        // Agent-to-Agent
}
```

---

## 8. Estado e Rastreamento por Nó

### NodeStateTracker

```
src/EfsAiHub.Host.Worker/Services/NodeStateTracker.cs
```

Gerencia o estado in-memory de cada nó durante a execução:

```csharp
ConcurrentDictionary<nodeId, NodeExecutionRecord>  // snapshots imutáveis
ConcurrentDictionary<nodeId, StringBuilder>         // acumulação de tokens (evita O(N²))
Dictionary<agentId, Activity>                       // spans OpenTelemetry
string CurrentAgentId                               // agente ativo (detecção de handoff)
```

**Métodos principais:**
- `AppendOutput(nodeId, text)` — append bufferizado de tokens
- `MaterializeOutput(nodeId)` — copia StringBuilder para Record.Output
- `StartAgentSpan() / TryEndAgentSpan()` — ciclo de vida do tracing

### NodeExecutionRecord

```
src/EfsAiHub.Core.Orchestration/Workflows/NodeExecutionRecord.cs
```

```csharp
public class NodeExecutionRecord
{
    public required string NodeId { get; init; }
    public required string ExecutionId { get; init; }
    public string NodeType { get; init; } = "executor";   // "agent" | "executor" | "trigger"
    public string Status { get; init; } = "pending";      // "pending" | "running" | "completed" | "failed"
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? Output { get; init; }
    public bool OutputTruncated { get; init; }
    public int Iteration { get; init; } = 1;
    public int TokensUsed { get; init; }
}
```

### NodePersistenceService

```
src/EfsAiHub.Host.Worker/Services/NodePersistenceService.cs
```

Background service que consome `Channel<NodePersistenceJob>`:
- Persiste `NodeExecutionRecord` no banco
- Publica eventos `node_started` / `node_completed`
- Bounded channel (capacidade 2000), `DropOldest` em overflow

---

## 9. Human-in-the-Loop (HITL)

### Configuração por Nó

```
src/EfsAiHub.Core.Orchestration/Workflows/NodeHitlConfig.cs
```

```csharp
public class NodeHitlConfig
{
    public required string When { get; init; }          // "before" | "after"
    public InteractionType InteractionType { get; init; } = InteractionType.Approval;
    public required string Prompt { get; init; }        // Pergunta para o humano
    public bool ShowOutput { get; init; } = false;      // Inclui output do nó como contexto
    public IReadOnlyList<string>? Options { get; init; } // Opções customizadas (Choice)
    public int TimeoutSeconds { get; init; } = 300;     // Timeout independente
}
```

### Tipos de Interação

```csharp
public enum InteractionType
{
    Approval,   // Binário: Aprovar / Rejeitar
    Input,      // Texto livre
    Choice      // N opções predefinidas
}
```

### Fluxo de Aprovação

```
1. Nó agenda execução
2. HitlDecoratorExecutor intercepta (before/after)
3. Publica evento hitl_required via IWorkflowEventBus
4. Persiste HumanInteractionRequest no PostgreSQL (Status=Pending)
5. Bloqueia workflow via TaskCompletionSource
6. Humano recebe prompt via SSE (AG-UI)
7. Humano responde → HumanInteractionService.ResolveAsync()
   │
   └─ CAS: IHumanInteractionRepository.TryResolveAsync
          UPDATE ... WHERE Status='Pending' → rowsAffected=1 vence
8. Vencedor: TCS completado → workflow continua ou falha
   Perdedores (resolução concorrente): incrementa hitl.resolve_conflicts,
                                       limpa local sem duplicar efeito
```

### HitlDecoratorExecutor

```
src/EfsAiHub.Platform.Runtime/Hitl/HitlDecoratorExecutor.cs
```

Decorator que envolve qualquer executor:
- **`when="before"`** — pausa antes da execução; humano vê o input
- **`when="after"`** — pausa depois da execução; humano vê o output (se `showOutput=true`)
- Se rejeitado → `HitlRejectedException` → execução marcada como Failed com `ErrorCategory.HitlRejected`

### HitlResolutionClassifier

```
src/EfsAiHub.Platform.Runtime/Hitl/HitlResolutionClassifier.cs
```

Classifica a resposta humana:
- **JSON:** `{"approved": bool}` → parse direto
- **String:** Match contra termos de rejeição ("rejected", "rejeitar", "cancelado", "no", "timeout")
- **Default:** Aprovado (se nenhum termo de rejeição detectado)

### Recovery Cross-Pod

```
src/EfsAiHub.Host.Worker/Services/HitlRecoveryService.cs
```

Background service que recupera workflows pausados:
1. **Startup:** `RecoverAllAsync()` imediato
2. **Polling:** A cada `HitlRecoveryIntervalSeconds` (default 30s)
3. **Por execução:**
   - PostgreSQL advisory lock (previne recovery duplo)
   - Busca último `HumanInteractionRequest`
   - **Pending:** Re-registra TaskCompletionSource, aguarda humano
   - **Aprovado/Rejeitado:** Resolve TCS imediatamente
   - **Expirado:** Marca como Failed
4. Chama `WorkflowRunnerService.ResumeAsync()` a partir do checkpoint
5. Respeita back-pressure via `IExecutionSlotRegistry`

### Propagação Cross-Pod

Resolução HITL propagada via PostgreSQL LISTEN/NOTIFY (`ICrossNodeBus.PublishHitlResolvedAsync`).

### Idempotência — CAS na resolução

`HumanInteractionService.ResolveAsync` é **idempotente por CAS a nível de banco**. Três callers podem tentar resolver a mesma interação em paralelo:

1. **API local** (`POST /api/interactions/{id}/resolve` ou `POST /api/chat/ag-ui/resolve-hitl`)
2. **Cross-pod NOTIFY** (outro pod já resolveu e está propagando)
3. **Timeout HITL** (`request.TimeoutSeconds` expirou)

Apenas **um** vence o CAS — a chamada que altera `Status` de `Pending` para `Approved/Rejected/Expired` no PostgreSQL via `UPDATE ... WHERE Status = 'Pending'` retornando `rowsAffected > 0`. Os demais callers recebem `false` e limpam seu estado local (mas não duplicam o efeito): não disparam o TCS duas vezes, não propagam NOTIFY em loop, não incrementam o contador de resolved duas vezes.

**Observabilidade:**
- Métrica `hitl.resolved` incrementada apenas pelo vencedor (`outcome` = approved/rejected/expired).
- Métrica `hitl.resolve_conflicts` incrementada por quem **perdeu** o CAS (com tag `outcome`). Alto volume indica contenção alta — possível bug de caller chamando repetidamente.

**Contrato:** `TryResolveAsync` no `IHumanInteractionRepository` é o único mecanismo de atomicidade. Qualquer novo caller de resolução deve ir via `HumanInteractionService.ResolveAsync` (que encapsula o CAS), nunca tocar `UpdateAsync` diretamente para mudar Status.

### UX no frontend para CAS perdido

Quando um caller frontend (API `/resolve` ou chat `/resolve-hitl`) perde o CAS, o backend retorna **HTTP 404**. O `safeFetch` wrapper extrai `{error: "mensagem"}` e lança `ApiError(404, ...)`. A UI trata graciously:

- **`HitlResolvePage`** (`/hitl/:id`): mutation `useResolveInteraction` detecta 404 via `isHitlAlreadyResolvedError()` → exibe banner âmbar "Esta interação já foi resolvida por outro operador ou a execução expirou. Recarregando..." e `refetch()` para carregar o novo status. Evita navegar embora automaticamente para que o operador entenda o que aconteceu.
- **`ChatWindowPage.handleApproval`**: otimismo local é mantido (bubble mostra aprovação), mas uma `note` visual em âmbar "⚠ Já resolvido por outro operador" é adicionada para sinalizar divergência.
- **`useResolveInteraction`** invalida `KEYS.pending`, `KEYS.detail(id)` e `['interactions', 'execution']` em **ambos** `onSuccess` e `onError-404` para que qualquer tela aberta (detalhe da execução, lista pending, minha interação) seja atualizada.

Correlação com observabilidade: picos de métrica `hitl.resolve_conflicts` no backend correspondem a usuários recebendo o toast 404 no frontend — dashboards devem mostrar ambos lado a lado para debug de contenção.

---

## 10. Escalation e Roteamento

### RoutingRule

```
src/EfsAiHub.Core.Orchestration/Workflows/RoutingRule.cs
```

```csharp
public class RoutingRule
{
    public required string Match { get; init; }     // "category:x" | "tag:x" | "regex:pattern" | "any"
    public required string TargetNodeId { get; init; }
    public int Priority { get; init; } = 0;          // Maior prioridade vence
}
```

### Fluxo

```
Agente emite AgentEscalationSignal
    → Armazenado em ExecutionContext.EscalationSignals
    → EscalationRouter avalia regras em ordem de prioridade (decrescente)
    → Retorna primeiro TargetNodeId que faz match
    → Workflow continua no nó alvo
```

### Formatos de Match

| Formato | Exemplo | Comportamento |
|---------|---------|--------------|
| `category:X` | `category:billing` | Match exato na categoria do sinal |
| `tag:X` | `tag:priority` | Match em tag do sinal |
| `regex:X` | `regex:^refund.*` | Regex no conteúdo do sinal |
| `any` | `any` | Catch-all |

**Ortogonal ao HITL:** Escalation é decisão do router (não-bloqueante); HITL é decisão humana (bloqueante).

---

## 11. Versionamento de Workflow

### WorkflowVersion

```
src/EfsAiHub.Core.Orchestration/Workflows/WorkflowVersion.cs
```

```csharp
public record WorkflowVersion
{
    public string WorkflowVersionId { get; init; }
    public string WorkflowDefinitionId { get; init; }
    public int Revision { get; init; }
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public string? ChangeReason { get; init; }
    public WorkflowVersionStatus Status { get; init; }
    public string ContentHash { get; init; }           // SHA-256 idempotência
    public string? DefinitionSnapshot { get; init; }   // JSON completo para rollback
}
```

### Status

```csharp
public enum WorkflowVersionStatus
{
    Draft,       // Em desenvolvimento
    Published,   // Ativa em produção
    Retired      // Aposentada
}
```

### Mecanismo

1. **Append-only:** Cada update cria nova versão com `Revision` incrementado
2. **Idempotência:** `ContentHash` (SHA-256) garante que definição idêntica não cria nova revisão
3. **Snapshot completo:** `DefinitionSnapshot` armazena JSON completo do `WorkflowDefinition`
4. **Rollback:** `POST /api/workflows/{id}/rollback` restaura de qualquer versão anterior

### Repository

```
src/EfsAiHub.Infra.Persistence/Postgres/PgWorkflowVersionRepository.cs
```

Métodos:
- `AppendAsync()` — append idempotente (verifica ContentHash)
- `GetCurrentAsync()` — versão mais recente
- `ListByDefinitionAsync()` — histórico completo
- `GetDefinitionSnapshotAsync()` — snapshot para rollback

---

---

## 12. Sandbox

```
src/EfsAiHub.Core.Abstractions/Execution/ExecutionMode.cs
```

```csharp
public enum ExecutionMode
{
    Production,   // Normal: persistência, billing, métricas produção
    Sandbox       // LLM real, tools mockados, sem persistência de mensagens
}
```

### Comportamento em Sandbox

| Aspecto | Production | Sandbox |
|---------|-----------|---------|
| **LLM** | Chamadas reais | Chamadas reais |
| **Tools** | Execução real | Mockados via `ToolMocker` |
| **Mensagens** | Persistidas no banco | Não persistidas |
| **Métricas** | `mode=production` | `mode=sandbox` |
| **TokenUsage** | Persistido normal | Persistido com `IsSandbox=true` |

### API

```
POST /api/workflows/{id}/sandbox
```

Chama `TriggerAsync()` com `mode: ExecutionMode.Sandbox`.

### Propagação

O `ExecutionMode` é propagado via `AsyncLocal<ExecutionContext>` e fica disponível para todas as camadas (middleware, provider, persistência) ajustarem seu comportamento.

---

## 13. Budget e Controle de Custo

### ExecutionBudget

```
src/EfsAiHub.Core.Agents/Execution/ExecutionContext.cs
```

```csharp
public class ExecutionBudget
{
    public int MaxTokensPerExecution { get; init; }   // Hard cap tokens
    public decimal? MaxCostUsd { get; init; }         // Hard cap USD (null = sem enforcement)
    public long TotalTokens { get; }                  // Interlocked read
    public decimal TotalCostUsd { get; }              // Lock-protected read
    public bool IsExceeded { get; }                   // Token OU custo excedido
    public bool IsCostExceeded { get; }               // Apenas custo

    public void Add(int tokens);                      // Thread-safe increment
    public decimal AddCost(decimal costUsd);           // Thread-safe accumulation
}
```

### Enforcement

1. **TokenTrackingChatClient** verifica `Budget.IsExceeded` antes de cada chamada LLM
2. Se excedido → `BudgetExceededException` → workflow falha com `ErrorCategory.BudgetExceeded`
3. **Compartilhado:** Budget único por execução, compartilhado entre todos os nós via `ExecutionContext` (AsyncLocal)
4. **Thread-safe:** Tokens via `Interlocked`, custo via `lock`

### BudgetExceededException

```
src/EfsAiHub.Platform.Guards/BudgetExceededException.cs
```

Inclui:
- `TotalTokens` / `MaxTokensPerExecution` — para budget de tokens
- `TotalCostUsd` / `MaxCostUsd` — para budget de custo
- `IsCostCause` — indica se o custo foi a causa

### Project Budget Guard

Budget diário por projeto usando Redis counters (`ProjectBudgetGuard`). Independente do budget por execução.

---

## 14. Middleware por Agente

```
src/EfsAiHub.Platform.Runtime/Factories/AgentFactory.cs
```

Cada agente dentro de um workflow recebe seu próprio pipeline de middleware. O pipeline é construído pelo `AgentFactory`:

### Pipeline Completo (C6)

```
Request →
  RetryingChatClient          // Retry com exponential backoff
    → CircuitBreakerChatClient // Failover por provider
      → [User Middlewares]     // AccountGuard, StructuredOutputState, etc.
        → TokenTrackingChatClient  // Budget enforcement + cost calc + bridge AG-UI
          → FunctionInvokingChatClient // Auto tool-call loop
            → Raw LLM Provider
```

### TokenTrackingChatClient como Bridge AG-UI

```
src/EfsAiHub.Platform.Runtime/Factories/TokenTrackingChatClient.cs
```

O `TokenTrackingChatClient` tem **responsabilidade dupla** — além de budget enforcement e tracking de tokens, ele é o **ponto de ponte entre o pipeline de execução e o protocolo AG-UI**:

1. **Budget enforcement:** Verifica `Budget.IsExceeded` antes de cada chamada LLM
2. **Token tracking:** Acumula tokens e custo USD no `ExecutionBudget` compartilhado
3. **Custo USD síncrono (decisão de design):** O cálculo de custo na linha 163 usa `.GetAwaiter().GetResult()` — este é um **design intencional**, não um anti-pattern. O custo precisa ser acumulado no budget **antes** da próxima chamada LLM para que `EnforceBudget()` funcione corretamente. Se fosse async fire-and-forget, a chamada seguinte poderia executar sem o custo atualizado, permitindo ultrapassar o budget. O `ModelPricingCache` usa cache L1 in-memory (ConcurrentDictionary), retornando em microsegundos no cenário normal. O `TrackUsage` é chamado **após** a resposta do LLM (não no path de request do usuário), portanto não impacta latência.
4. **Bridge AG-UI (tool call args):** Durante `GetStreamingResponseAsync()`, intercepta cada chunk de `FunctionCallContent` e escreve em tempo real no `IAgUiTokenSink`:

```csharp
// Dentro de GetStreamingResponseAsync — linhas 91-102
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

Isso permite que o frontend receba `TOOL_CALL_ARGS` enquanto o LLM ainda está gerando os argumentos — sem esperar a chamada completa.

**Fluxo:**
```
LLM streaming → FunctionCallContent chunk
    → TokenTrackingChatClient intercepta
    → IAgUiTokenSink.WriteToolCallArgs()
    → AgUiTokenChannel (in-memory bounded channel)
    → AgUiStreamMerger (merge com Event Bus)
    → SSE → TOOL_CALL_ARGS para o frontend
```

A injeção do `IAgUiTokenSink` é **opcional** (nullable) — quando o middleware opera fora do contexto AG-UI (ex: background jobs sem SSE), o sink é null e o bridging é ignorado.

### Dois Modos de Wrapping

| Modo | Quando | Pipeline |
|------|--------|----------|
| `WrapWithTokenTracking()` | Agentes em workflow (Sequential, Concurrent, Handoff, GroupChat) | Pipeline completo (C6) |
| `WrapWithMiddlewares()` | Graph mode (AIAgents) | Apenas middlewares (sem Retry/CircuitBreaker no nível factory) |

### Isolamento

- Cada nó-agente tem instância própria dos middlewares
- Diferentes nós podem ter providers, middlewares e configurações distintos
- O budget (`ExecutionBudget`) é compartilhado entre todos os nós via `ExecutionContext`

---

## 15. Providers por Nó

### Resolução de Provider

```
src/EfsAiHub.Platform.Runtime/Factories/AgentFactory.cs
```

Cada agente referenciado no workflow especifica seu provider na `AgentDefinition.Provider.Type`:

| Provider | SDK |
|----------|-----|
| `OPENAI` | OpenAI SDK (v2.10.0) |
| `AZUREOPENAI` | Azure.AI.OpenAI (v2.9.0-beta.1) |
| `AZUREFOUNDRY` | Azure.AI.Agents.Persistent (v1.2.0-beta.10) |

### Override por Projeto

`InjectProjectCredentials()` permite que o projeto sobreponha as credenciais do provider do agente. Isso habilita:
- Mesmo agente, providers diferentes por projeto
- Isolamento de billing entre mesas de operação

### Circuit Breaker com Fallback

O `CircuitBreakerChatClient` suporta provider de fallback:
- Se o provider primário abrir o circuit breaker (5 falhas), as requisições são roteadas para o provider de fallback
- O fallback pode ser de tipo diferente (ex: OPENAI → AZUREOPENAI)

---

## 16. Event Bus e Auditoria

### IWorkflowEventBus

```
src/EfsAiHub.Core.Orchestration/Workflows/IWorkflowEventBus.cs
```

```csharp
public interface IWorkflowEventBus
{
    Task PublishAsync(WorkflowEventEnvelope envelope);
    IAsyncEnumerable<WorkflowEventEnvelope> SubscribeAsync(string executionId, CancellationToken ct);
    Task<List<WorkflowEventEnvelope>> GetHistoryAsync(string executionId);
}
```

### WorkflowEventEnvelope

```csharp
public class WorkflowEventEnvelope
{
    public required string EventType { get; init; }    // Tipo do evento
    public required string ExecutionId { get; init; }
    public required string Payload { get; init; }      // JSON
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public long SequenceId { get; init; } = 0;         // Auto-gerado pelo banco; 0 = não persistido
}
```

### Tipos de Evento

| EventType | Persistido | Descrição |
|-----------|-----------|-----------|
| `workflow_started` | Sim | Execução iniciou |
| `workflow_completed` | Sim | Execução concluiu com sucesso |
| `node_started` | Sim | Nó iniciou execução |
| `node_completed` | Sim | Nó completou com output |
| `handoff` | Sim | Handoff entre agentes |
| `hitl_required` | Sim | HITL aguardando decisão humana |
| `state_delta` | Sim | Delta de estado compartilhado |
| `error` | Sim | Erro na execução |
| `token` | Não | Token de streaming LLM (alto volume, não persiste) |

### PgEventBus (Implementação)

```
src/EfsAiHub.Infra.Messaging/PgEventBus.cs
```

Outbox pattern com PostgreSQL:

1. **Publish (eventos não-token):**
   - INSERT em `workflow_event_audit` → recebe `SequenceId` auto-incremento
   - `pg_notify` com referência ao evento

2. **Publish (token):**
   - `pg_notify` com payload completo (pequeno, não persiste)

3. **Subscribe (SSE):**
   - `LISTEN` no channel `wf_{executionId_hex}`
   - Replay do histórico via `GetAllAsync()`
   - Dedup por `SequenceId` via `HashSet`
   - Termina em `workflow_completed` ou `error`

**Decisão de design — Channel unbounded no Subscribe:**
O `liveChannel` interno do `SubscribeAsync()` (linha 94) usa `Channel.CreateUnbounded`. Isto é seguro porque:
- O channel é criado **por subscrição** (não global) — cada assinante SSE tem o seu
- Um workflow produz dezenas a centenas de eventos, não milhares
- O reader (`await foreach` na linha 179) consome imediatamente — sem backlog acumulado
- Quando `workflow_completed` ou `error` chega, o loop termina e o channel é disposed
- O `linkedCts` + `finally` garante cleanup mesmo em desconexão abrupta
- Eventos de token (alto volume) são pequenos (~100 bytes) e resolvidos sem DB

---

## 17. AG-UI e Streaming SSE

```
src/EfsAiHub.Host.Api/Chat/AgUi/
```

### Endpoints

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/chat/ag-ui/stream` | Inicia stream SSE |
| POST | `/api/chat/ag-ui/cancel` | Cancela execução |
| POST | `/api/chat/ag-ui/resolve-hitl` | Resolve HITL inline |
| GET | `/api/chat/ag-ui/reconnect/{executionId}` | Reconexão com resync |

### Fluxo SSE

```
Cliente conecta → SSE stream aberto
    → workflow_started
    → node_started (agente A)
    → token, token, token...    (streaming LLM)
    → node_completed (agente A)
    → hitl_required             (se HITL configurado)
    → [aguarda resolução humana]
    → node_started (agente B)
    → token, token, token...
    → node_completed (agente B)
    → workflow_completed
```

### Componentes

- **AgUiSseHandler** — Gerencia conexões SSE, dedup por SequenceId
- **AgUiStreamMerger** — Merge de múltiplos streams (execução + tokens)
- **AgUiSharedStateWriter** — Atualiza estado compartilhado do UI
- **AgUiEventMapper** — Mapeia eventos do framework para formato AG-UI
- **AgUiReconnectionHandler** — Resync em reconexão (replay de histórico)

---

## 18. Rate Limiting e Concorrência

### Rate Limiting por Usuário e Conversa

```
src/EfsAiHub.Host.Api/Chat/Services/ChatRateLimiter.cs
```

Redis Sorted Set com sliding window (Lua script atômico):

| Limite | Default | Chave Redis |
|--------|---------|-------------|
| Per-user | 10 msgs / 60s | `rl:chat:{userId}` |
| Per-conversation | 5 msgs / 60s | `rl:chat:conv:{conversationId}` |

### Rate Limiting por Projeto

```
src/EfsAiHub.Host.Api/Chat/Services/ProjectRateLimiter.cs
```

| Limite | Default | Chave Redis |
|--------|---------|-------------|
| Per-project | Configurável | `rl:project:{projectId}` |

Lê `ProjectSettings.MaxRequestsPerMinute`. Null/0 = sem enforcement.

### Concorrência por Workflow

`WorkflowConfiguration.MaxConcurrentExecutions`:
- `null` → ilimitado
- `> 0` → semáforo por workflow (guard local + distribuído no banco)

### Back-Pressure Global

| Path | Mecanismo | Limite Default |
|------|-----------|---------------|
| Chat | `IExecutionSlotRegistry` | 200 |
| Interactive | SemaphoreSlim + DB guard | 10 |
| Background | SemaphoreSlim + DB guard | 10 |

---

## 19. Enrichment e Disclaimers

### EnrichmentRule

```
src/EfsAiHub.Core.Agents/Enrichment/EnrichmentRule.cs
```

```csharp
public class EnrichmentRule
{
    public EnrichmentCondition When { get; init; }      // Condição de match
    public string? AppendDisclaimer { get; init; }      // Chave do DisclaimerRegistry
    public Dictionary<string, string>? Defaults { get; init; }  // Defaults com source
}

public class EnrichmentCondition
{
    public string? ResponseType { get; init; }   // null = match all
}
```

---

## 20. Checkpoint e Recovery

### Modos de Checkpoint

| Modo | Armazenamento | Quando usar |
|------|--------------|-------------|
| `InMemory` | Memória do processo | Development, workflows curtos |
| `Postgres` | PostgreSQL | HITL com recovery cross-pod |
| `Blob` | Azure Blob Storage | Workflows de longa duração |

### IEngineCheckpointAdapter

Persiste estado do workflow para recovery:
- **Save:** Serializa estado atual em checkpoint nomeado
- **Resume:** `TryResumeAsync()` restaura de checkpoint para continuar execução
- Usado pelo `HitlRecoveryService` para retomar workflows pausados

### Retenção

`CheckpointRetentionDays` (default 14) — checkpoints expirados são limpos periodicamente.

---

## 21. Observabilidade

### Activity Sources

```
src/EfsAiHub.Infra.Observability/Tracing/ActivitySources.cs
```

| ActivitySource | Escopo |
|---------------|--------|
| `WorkflowExecution` | Span de execução do workflow |
| `AgentInvocation` | Span por agente |
| `LlmCall` | Span por chamada LLM |
| `ToolCall` | Span por invocação de tool |

### Correlation IDs

- `execution.id` tagueado em activities de workflow
- TraceId/SpanId propagados automaticamente via `Activity`
- Correlaciona workflow → agente → LLM → tool em uma trace completa

### Métricas

`MetricsRegistry` rastreia:
- Token usage por execução
- Budget exceeded
- Escalation signals
- HITL: requested, resolved, resolution duration, pending age
- Node: started, completed, failed, duration
- Execution: duration, status, concurrent count

### Tracing por Nó

`NodeStateTracker` gerencia spans OpenTelemetry:
- `StartAgentSpan()` — abre span quando agente inicia
- `TryEndAgentSpan()` — fecha span quando agente completa
- Tags: `agent.id`, `agent.name`, `execution.id`, `tokens_used`

---

## 22. Categorias de Erro

```
src/EfsAiHub.Core.Orchestration/Enums/ErrorCategory.cs
```

```csharp
public enum ErrorCategory
{
    Unknown,                    // Erro não classificado
    Timeout,                    // Timeout da execução
    AgentLoopLimit,             // Excedeu MaxAgentInvocations
    LlmError,                  // Erro genérico do LLM
    LlmRateLimit,              // Rate limit do provider
    LlmContentFilter,          // Filtro de conteúdo do provider
    ToolError,                  // Erro em tool
    Cancelled,                  // Cancelamento manual
    FrameworkError,             // Erro do framework Microsoft.Agents.AI
    BackPressureRejected,       // Rejeitado por back-pressure
    BudgetExceeded,             // Token ou custo excedido
    CheckpointRecoveryFailed,   // Falha ao restaurar checkpoint
    HitlRejected               // Humano rejeitou no HITL
}
```

### ExecutionFailureWriter

```
src/EfsAiHub.Platform.Runtime/Workflows/ExecutionFailureWriter.cs
```

Encapsula transições terminais:
- `MarkFailedAsync(execution, message, category)` → Status=Failed
- `MarkCancelledAsync(execution, isTimeout)` → Status=Cancelled (ou Timeout)
- `MarkCompletedAsync(execution, output)` → Status=Completed
- Persiste → publica evento → notifica `IExecutionLifecycleObserver`

---

## 23. API REST

```
src/EfsAiHub.Host.Api/Controllers/WorkflowsController.cs
```

### CRUD

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/workflows` | Criar workflow |
| GET | `/api/workflows` | Listar workflows |
| GET | `/api/workflows/{id}` | Obter workflow por ID |
| PUT | `/api/workflows/{id}` | Atualizar workflow |
| DELETE | `/api/workflows/{id}` | Remover workflow |

### Execução

| Método | Path | Descrição |
|--------|------|-----------|
| POST | `/api/workflows/{id}/trigger` | Disparar execução (202 + executionId) |
| POST | `/api/workflows/{id}/sandbox` | Executar em sandbox |
| GET | `/api/workflows/{id}/executions` | Listar execuções (paginado, filtrável por status) |

### Versionamento

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/workflows/{id}/versions` | Listar versões (mais recente primeiro) |
| GET | `/api/workflows/{id}/versions/{versionId}` | Obter versão específica |
| POST | `/api/workflows/{id}/rollback` | Rollback para versão anterior |

### Catálogo e Utilitários

| Método | Path | Descrição |
|--------|------|-----------|
| GET | `/api/workflows/visible` | Listar workflows visíveis (projeto + global) |
| POST | `/api/workflows/{id}/clone` | Clonar workflow para projeto atual |
| POST | `/api/workflows/{id}/validate` | Validar sem persistir |
| GET | `/api/workflows/{id}/diagram` | Gerar diagrama PNG (Graphviz/mermaid) |

### DTOs

- **CreateWorkflowRequest** → `ToDomain()` para WorkflowDefinition
- **TriggerWorkflowRequest** → `{ Input, Metadata }`
- **WorkflowResponse** → `FromDomain()` do WorkflowDefinition
- **WorkflowVersionResponse** → `FromDomain()` do WorkflowVersion
- **ExecutionResponse** / **ExecutionDetailResponse** → `FromDomain()` do WorkflowExecution
- **RollbackWorkflowRequest** → `{ VersionId }`
- **CloneWorkflowRequest** → `{ NewId? }`

---

## 24. Persistência

### Repositórios PostgreSQL

```
src/EfsAiHub.Infra.Persistence/Postgres/
```

| Interface | Tabela | Descrição |
|-----------|--------|-----------|
| `IWorkflowDefinitionRepository` | `WorkflowDefinitions` | CRUD de definições |
| `IWorkflowVersionRepository` | `WorkflowVersions` | Snapshots imutáveis |
| `IWorkflowExecutionRepository` | `WorkflowExecutions` | Tracking de execuções |
| `INodeExecutionRepository` | `NodeExecutions` | Tracking por nó |
| `IHumanInteractionRepository` | `HumanInteractions` | Interações HITL |
| `IWorkflowEventRepository` | `WorkflowEventAudits` | Auditoria de eventos |

### Cache Redis

| Chave | TTL | Descrição |
|-------|-----|-----------|
| `workflow-def:{id}` | 5 min | Cache read-through de definições |

Invalidação automática em `UpsertAsync()`.

### Channels In-Memory

| Channel | Capacidade | Consumer |
|---------|-----------|----------|
| `NodePersistenceChannel` | 2000 | `NodePersistenceService` |

---

## 25. Como Criar um Workflow

### Passo 1: Definir os Agentes

Cada agente referenciado precisa existir no catálogo. Veja [docs/agentes.md](agentes.md) para como criar agentes.

### Passo 2: Escolher o Modo de Orquestração

| Cenário | Modo |
|---------|------|
| Pipeline linear (A→B→C) | `Sequential` |
| Análises paralelas independentes | `Concurrent` |
| Manager + especialistas com delegação | `Handoff` |
| Discussão multi-agente moderada | `GroupChat` |
| Topologia customizada com código e condições | `Graph` |

### Passo 3: Criar via API

**Exemplo — Sequential simples:**

```json
POST /api/workflows
{
  "id": "analise-risco-pipeline",
  "name": "Pipeline de Análise de Risco",
  "orchestrationMode": "Sequential",
  "agents": [
    { "agentId": "extrator-dados" },
    { "agentId": "analista-risco" },
    { "agentId": "formatador-relatorio" }
  ],
  "configuration": {
    "maxTokensPerExecution": 30000,
    "timeoutSeconds": 120
  }
}
```

**Exemplo — Graph com FanOut/FanIn e HITL:**

```json
POST /api/workflows
{
  "id": "analise-completa",
  "name": "Análise Completa com Aprovação",
  "orchestrationMode": "Graph",
  "agents": [
    { "agentId": "classificador" },
    { "agentId": "analista-credito" },
    { "agentId": "analista-mercado" },
    {
      "agentId": "consolidador",
      "hitl": {
        "when": "after",
        "prompt": "Revise a análise consolidada. Aprovar para envio?",
        "interactionType": "Approval",
        "showOutput": true,
        "timeoutSeconds": 600
      }
    }
  ],
  "edges": [
    {
      "from": "classificador",
      "targets": ["analista-credito", "analista-mercado"],
      "edgeType": "FanOut"
    },
    {
      "to": "consolidador",
      "sources": ["analista-credito", "analista-mercado"],
      "edgeType": "FanIn"
    }
  ],
  "configuration": {
    "enableHumanInTheLoop": true,
    "maxTokensPerExecution": 50000,
    "maxCostUsdPerExecution": 2.50,
    "checkpointMode": "Postgres"
  }
}
```

### Passo 4: Validar

```
POST /api/workflows/analise-completa/validate
```

Retorna erros de validação sem persistir.

### Passo 5: Disparar

```json
POST /api/workflows/analise-completa/trigger
{
  "input": "Analisar carteira do cliente 12345",
  "metadata": { "source": "backoffice" }
}
```

Resposta: `202 Accepted` com `{ "executionId": "exec-abc123" }`

### Passo 6: Acompanhar via SSE

```
POST /api/chat/ag-ui/stream
{ "executionId": "exec-abc123" }
```

Eventos SSE em tempo real: `workflow_started → node_started → token → ... → workflow_completed`

### Checklist de Criação

- [ ] Agentes existem no catálogo
- [ ] Modo de orquestração adequado ao cenário
- [ ] Budget configurado (`maxTokensPerExecution`, `maxCostUsdPerExecution`)
- [ ] Timeout adequado (`timeoutSeconds`)
- [ ] HITL configurado onde necessário (nós críticos)
- [ ] Edges definidos (Graph/Handoff)
- [ ] Validação executada sem erros
- [ ] Enrichment rules para compliance (se aplicável)
- [ ] Checkpoint mode adequado (Postgres para HITL cross-pod)
- [ ] Concurrency limit se necessário (`maxConcurrentExecutions`)

---

## 26. Workflows Cadastrados

### `classificacao-fato-relevante`

| Campo | Valor |
|-------|-------|
| **Name** | Classificação de Fato Relevante |
| **Mode** | Graph |
| **Description** | Recebe PDF de fato relevante, extrai texto via Document Intelligence e classifica com score, categoria e resumo |
| **InputMode** | Standalone |
| **Account** | 011982329 |
| **HITL** | disabled |
| **Timeout** | 300s |

**Agents:**

| AgentId | Papel |
|---------|-------|
| `classificador-fato-relevante` | Classificador |

**Executors:**

| NodeId | Tipo |
|--------|------|
| `pdf-extract` | document_intelligence |

**Edges:**

| From | To | Tipo |
|------|----|------|
| `pdf-extract` | `classificador-fato-relevante` | Direct |

**OutputNodes:** `[classificador-fato-relevante]`

```json
POST /api/workflows
{
  "id": "classificacao-fato-relevante",
  "name": "Classificação de Fato Relevante",
  "orchestrationMode": "Graph",
  "agents": [
    { "agentId": "classificador-fato-relevante" }
  ],
  "executors": [
    { "nodeId": "pdf-extract", "type": "document_intelligence" }
  ],
  "edges": [
    {
      "from": "pdf-extract",
      "to": "classificador-fato-relevante",
      "edgeType": "Direct"
    }
  ],
  "outputNodes": ["classificador-fato-relevante"],
  "configuration": {
    "inputMode": "Standalone",
    "enableHumanInTheLoop": false,
    "timeoutSeconds": 300
  }
}
```

---

## Referência Rápida de Arquivos

| Componente | Arquivo |
|-----------|---------|
| Definição | `src/EfsAiHub.Core.Orchestration/Workflows/WorkflowDefinition.cs` |
| Execução | `src/EfsAiHub.Core.Orchestration/Workflows/WorkflowExecution.cs` |
| Versão | `src/EfsAiHub.Core.Orchestration/Workflows/WorkflowVersion.cs` |
| HITL Config | `src/EfsAiHub.Core.Orchestration/Workflows/NodeHitlConfig.cs` |
| HITL Request | `src/EfsAiHub.Core.Orchestration/Workflows/HumanInteractionRequest.cs` |
| Node Record | `src/EfsAiHub.Core.Orchestration/Workflows/NodeExecutionRecord.cs` |
| Event Bus | `src/EfsAiHub.Core.Orchestration/Workflows/IWorkflowEventBus.cs` |
| Routing | `src/EfsAiHub.Core.Orchestration/Workflows/RoutingRule.cs` |
| Enums | `src/EfsAiHub.Core.Orchestration/Enums/` |
| Service | `src/EfsAiHub.Platform.Runtime/Application/Services/WorkflowService.cs` |
| Validator | `src/EfsAiHub.Platform.Runtime/Application/Services/WorkflowValidator.cs` |
| Factory | `src/EfsAiHub.Platform.Runtime/Factories/WorkflowFactory.cs` |
| Edge Handlers | `src/EfsAiHub.Platform.Runtime/Factories/EdgeHandlers.cs` |
| Executor | `src/EfsAiHub.Host.Worker/Services/WorkflowExecutor.cs` |
| Runner | `src/EfsAiHub.Host.Worker/Services/WorkflowRunnerService.cs` |
| Agent Handoff Handler | `src/EfsAiHub.Host.Worker/Services/EventHandlers/AgentHandoffEventHandler.cs` |
| State Tracker | `src/EfsAiHub.Host.Worker/Services/NodeStateTracker.cs` |
| Node Persist | `src/EfsAiHub.Host.Worker/Services/NodePersistenceService.cs` |
| Failure Writer | `src/EfsAiHub.Platform.Runtime/Workflows/ExecutionFailureWriter.cs` |
| HITL Decorator | `src/EfsAiHub.Platform.Runtime/Hitl/HitlDecoratorExecutor.cs` |
| HITL Recovery | `src/EfsAiHub.Host.Worker/Services/HitlRecoveryService.cs` |
| Event Bus Impl | `src/EfsAiHub.Infra.Messaging/PgEventBus.cs` |
| Controller | `src/EfsAiHub.Host.Api/Controllers/WorkflowsController.cs` |
| AG-UI | `src/EfsAiHub.Host.Api/Chat/AgUi/` |
| Persistence | `src/EfsAiHub.Infra.Persistence/Postgres/Pg*Repository.cs` |
