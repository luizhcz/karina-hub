# EfsAiHub

Plataforma para orquestração de agentes de IA com suporte a múltiplos modos de execução, chat conversacional com streaming SSE (AG-UI), function tools extensíveis, middlewares LLM, executores de código, HITL (human-in-the-loop) e multi-tenancy por projeto. Construída sobre .NET 8 + Microsoft.Extensions.AI + Microsoft.Agents.AI.Workflows.

---

## O que faz

O EfsAiHub permite definir **workflows** que coordenam um ou mais agentes de IA. Cada workflow roda em um dos cinco modos de orquestração:

| Modo | Descrição |
|---|---|
| **Sequential** | Agentes executam um após o outro, em ordem |
| **Concurrent** | Agentes executam em paralelo |
| **Handoff** | Um agente transfere o controle para outro dinamicamente |
| **GroupChat** | Múltiplos agentes colaboram em uma conversa compartilhada |
| **Graph** | Grafo dirigido com edges explícitas — mistura agentes e executores de código puro |

A plataforma expõe uma API REST + streaming SSE (protocolo AG-UI) para gerenciar agentes, workflows, skills, conversas e execuções, com um frontend React completo.

---

## Arquitetura

```
                   ┌──────────────────────────────────────────┐
                   │           Frontend (React 19 + Vite)      │  :3000
                   │  Tailwind CSS · Zustand · TanStack Query  │
                   │  Monaco Editor · xyflow · Recharts         │
                   │                                            │
                   │  Páginas: Chat, Agents, Workflows, Skills, │
                   │  Executions, HITL, Metrics, Costs, Audit,  │
                   │  Projects, Config, Background Jobs         │
                   └─────────────────┬────────────────────────┘
                                     │ HTTP + SSE
                                     ▼
┌───────────────────────────────────────────────────────────────────────┐
│                    EfsAiHub.Host.Api (.NET 8)                  :5189  │
│                                                                       │
│  ┌─────────────────────┐  ┌────────────────────────────────────────┐ │
│  │   AG-UI SSE Stream   │  │          REST API (Swagger)            │ │
│  │  /api/chat/ag-ui/*   │  │  /api/agents · /api/workflows         │ │
│  │  Token streaming     │  │  /api/conversations · /api/executions  │ │
│  │  Tool calls          │  │  /api/skills · /api/projects           │ │
│  │  HITL inline         │  │  /api/interactions · /api/analytics    │ │
│  │  Reconnect           │  │  /api/model-catalog · /api/functions   │ │
│  └─────────────────────┘  │  /api/admin/model-pricing              │ │
│                            │  /api/admin/conversations              │ │
│                            │  /api/admin/token-usage                │ │
│                            └────────────────────────────────────────┘ │
│                                                                       │
│  ┌─── Middleware Pipeline ──────────────────────────────────────────┐ │
│  │  TenantMiddleware → ProjectMiddleware → DefaultProjectGuard     │ │
│  │  → ProjectRateLimitMiddleware → AdminGateMiddleware              │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  ┌─── Workflow Engine ─────────────────────────────────────────────┐ │
│  │  Sequential · Concurrent · Handoff · GroupChat · Graph          │ │
│  │  ChatTurnContextMapper (history → LLM messages)                 │ │
│  │  Checkpointing (InMemory | Postgres)                            │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  ┌─── Pontos de extensão (padrão registry) ────────────────────────┐ │
│  │  ├── IFunctionToolRegistry    (tools chamáveis pelo LLM)        │ │
│  │  ├── IAgentMiddlewareRegistry (interceptação de chamadas LLM)   │ │
│  │  └── ICodeExecutorRegistry    (nós C# puros no Graph)           │ │
│  └─────────────────────────────────────────────────────────────────┘ │
│                                                                       │
│  ┌─── Guards ──────────────────────────────────────────────────────┐ │
│  │  ProjectRateLimiter · ProjectBudgetGuard · LlmCircuitBreaker    │ │
│  └─────────────────────────────────────────────────────────────────┘ │
└──────────┬───────────────────┬────────────────────┬──────────────────┘
           │                   │                    │
           │          ┌────────┘                    │
           ▼          ▼                             ▼
  ┌──────────────┐  ┌──────────────┐    ┌──────────────────────────────┐
  │  PostgreSQL   │  │    Redis     │    │  EfsAiHub.Host.Worker        │
  │  :5432        │  │    :6379     │    │  (Background Services)       │
  │               │  │              │    │                              │
  │  3 pools:     │  │  Cache       │    │  WorkflowDispatcher          │
  │  · general    │  │  Rate limit  │    │  BackgroundWorkflowDispatcher│
  │  · sse        │  │  SSE fan-out │    │  ScheduledWorkflowService    │
  │  · reporting  │  │  Data Prot.  │    │  CrossNodeCoordinator        │
  │               │  │  AG-UI State │    │  HitlRecoveryService         │
  │  LISTEN/      │  │              │    │  LlmCostRefreshService       │
  │  NOTIFY       │  │              │    │  AuditRetentionService       │
  │  (event bus)  │  │              │    │  AgentSessionCleanup         │
  └──────────────┘  └──────────────┘    └──────────────────────────────┘
```

---

## Projetos do backend (src/)

| Camada | Projetos | Responsabilidade |
|---|---|---|
| **Core** | `Core.Abstractions`, `Core.Orchestration`, `Core.Agents` | Domínio, interfaces, modelos de orquestração, validação |
| **Plataforma** | `Platform.Runtime`, `Platform.Queue`, `Platform.Guards` | Registries (tools, middlewares, executores), filas, rate limiting, budget guards |
| **Infraestrutura** | `Infra.Persistence`, `Infra.Messaging`, `Infra.LlmProviders`, `Infra.Observability`, `Infra.Tools` | Postgres (EF Core + raw SQL), event bus (LISTEN/NOTIFY), LLM providers (Azure Foundry + MCP registry), OpenTelemetry |
| **Host** | `Host.Api`, `Host.Worker` | API principal + AG-UI SSE, background jobs + scheduling |

---

## Frontend (React 19 + Vite)

| Stack | Versão |
|---|---|
| React | 19 |
| Vite | 8 |
| TypeScript | 5.9 |
| Tailwind CSS | 4.2 |
| Zustand | 5 (state management) |
| TanStack Query | 5 (data fetching) |
| TanStack Table | 8 (tabelas de dados) |
| xyflow | 12 (visualização de grafos) |
| Monaco Editor | 4.7 (edição de código/prompts) |
| Recharts | 2.15 (gráficos de métricas) |

### Páginas principais

| Rota | Funcionalidade |
|---|---|
| `/chat` | Chat conversacional com streaming SSE, syntax highlighting, HITL inline |
| `/agents` | CRUD de agentes, versionamento, sandbox, comparação de versões |
| `/workflows` | CRUD de workflows, editor visual de grafos, trigger, sandbox |
| `/executions` | Lista de execuções, detalhe com streaming em tempo real |
| `/hitl` | Aprovações pendentes e histórico de interações humanas |
| `/skills` | CRUD de skills reutilizáveis com versionamento |
| `/mcp-servers` | CRUD de servidores MCP (Model Context Protocol) referenciados por agents — [docs/mcp.md](docs/mcp.md) |
| `/metrics` | Sumário de execuções, séries temporais, métricas por agente/provider |
| `/costs` | Dashboard de custos, por workflow, por projeto, catálogo de modelos |
| `/audit` | Trail de auditoria, consumo de tokens |
| `/projects` | Gestão de projetos (multi-tenancy) |
| `/background` | Jobs em background, agendamento de workflows |

---

## Sistema de conversas (Chat + AG-UI)

O chat funciona com streaming SSE usando o protocolo AG-UI:

1. **Frontend** envia apenas a mensagem atual (`POST /api/chat/ag-ui/stream`)
2. **Backend** carrega o histórico do banco (últimas N mensagens, configurável por workflow via `MaxHistoryMessages`, default 20)
3. **Backend** monta o `ChatTurnContext` (mensagem atual + histórico + metadata) e dispara o workflow
4. **ChatTurnContextMapper** converte o contexto em mensagens para o LLM conforme o modo de orquestração
5. **SSE** transmite tokens incrementais, tool calls, steps e eventos HITL em tempo real
6. **"Limpar Contexto"** seta `ContextClearedAt` na conversa — mensagens anteriores ficam visíveis mas não vão para o LLM

---

## Multi-tenancy e projetos

| Header | Finalidade |
|---|---|
| `x-efs-tenant-id` | Identifica o tenant (organização) |
| `x-efs-project-id` | Identifica o projeto (ou "default") |
| `x-efs-account` | Identifica a conta do usuário |

- Cada projeto pode ter LLM config própria, budget e rate limits
- O projeto `default` é restrito a contas admin (configuradas em `Admin:AccountIds`)
- Workflows, agentes e skills são isolados por projeto

---

## Pools de conexão PostgreSQL

O backend usa três pools isolados de `NpgsqlDataSource` para evitar contenção:

| Pool | Uso | Config |
|---|---|---|
| **general** | Chat path + writes do hot path | `Npgsql:GeneralMinPoolSize` / `GeneralMaxPoolSize` |
| **sse** | Conexões LISTEN de longa duração (SSE subscribers) | `Npgsql:SseMaxPoolSize` |
| **reporting** | Queries analíticas (futuro: read replica) | `Npgsql:ReportingMaxPoolSize` |

Repositórios como `PgProjectRepository` e `PgModelCatalogRepository` usam raw SQL via `NpgsqlDataSource` diretamente, enquanto o restante usa EF Core via `IDbContextFactory<AgentFwDbContext>`.

---

## Início rápido

### Pré-requisitos

- [Docker](https://docs.docker.com/get-docker/) + Docker Compose
- Uma chave de API OpenAI (ou endpoint Azure AI)

### 1. Configurar o ambiente

Copie e edite o arquivo de variáveis de ambiente:

```bash
cp .env.example .env
```

Defina suas credenciais LLM no `.env`:

```env
# OpenAI
OPENAI_API_KEY=sk-proj-...
OPENAI_DEFAULT_MODEL=gpt-4o

# Ou Azure AI
AZURE_AI_ENDPOINT=https://{SEU_HUB}.services.ai.azure.com/...
AZURE_AI_DEPLOYMENT=gpt-4o
```

### 2. Subir o stack

```bash
docker compose up
```

| Serviço | URL |
|---|---|
| Frontend | http://localhost:3000 |
| API | http://localhost:5189 |
| Swagger | http://localhost:5189/swagger |
| PostgreSQL | localhost:5432 |
| Redis | localhost:6379 |

### 3. Rodar localmente (sem Docker)

```bash
# Backend
cd src/EfsAiHub.Host.Api
dotnet run

# Frontend
cd frontend
npm install
npm run dev
```

---

## Configuração

Principais seções do `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=efs_ai_hub;...",
    "PostgresReporting": ""
  },
  "WorkflowEngine": {
    "MaxConcurrentExecutions": 10,
    "ChatMaxConcurrentExecutions": 200,
    "QueueCapacity": 500,
    "DefaultTimeoutSeconds": 300,
    "CheckpointMode": "InMemory",
    "EnableScheduledWorkflows": true,
    "HitlRecoveryConcurrency": 4,
    "AuditRetentionDays": 30,
    "ToolInvocationRetentionDays": 14,
    "CheckpointRetentionDays": 14
  },
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
  },
  "Admin": {
    "AccountIds": ["011982329"]
  },
  "CircuitBreaker": {
    "Enabled": true,
    "FailureThreshold": 5,
    "OpenDurationSeconds": 30,
    "HalfOpenTimeoutSeconds": 10,
    "EffectiveReplicaCount": 1
  },
  "OpenTelemetry": {
    "ServiceName": "efs-ai-hub",
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

### Deploy multi-pod — `CircuitBreaker:EffectiveReplicaCount`

O circuit breaker LLM mantém estado **per-process** (em memória). Em deploys com N réplicas, cada pod
conta falhas independentemente — na prática o sistema aceita até N× o `FailureThreshold` antes de
todos os pods abrirem. Para compensar, ajuste `EffectiveReplicaCount` ao número de réplicas:

| Cenário | Config recomendada |
|---|---|
| 1 pod (dev/single-pod) | `"EffectiveReplicaCount": 1` (default) |
| 2-3 pods (produção atual) | `"EffectiveReplicaCount": 3` — threshold efetivo = `FailureThreshold / 3` |
| ≥4 pods | migrar estado para Redis (backlog — issue aberta) |

O threshold efetivo nunca fica abaixo de 1 (garantia do código).

### Timeout + jitter por agente — `ResiliencePolicy`

Cada agente pode ter sua própria `ResiliencePolicy` no JSON da definição. Além dos campos clássicos
(`MaxRetries`, `InitialDelayMs`, `BackoffMultiplier`, `RetriableHttpStatusCodes`), o C4 adiciona:

| Campo | Default | Recomendação produção | Função |
|---|---|---|---|
| `CallTimeoutMs` | `null` | `60000` (60s) | Timeout máximo por tentativa individual. Se o provider pendurar, a chamada é abortada e retry entra. Em streaming, aplica só à conexão inicial. |
| `JitterRatio` | `0.0` | `0.1` | Jitter aleatório sobre o delay de backoff (`delay + rand(0, delay * ratio)`) para evitar thundering herd em recovery. |

Ver `docs/agentes.md#91-retryingchatclient` para detalhes. Tag nova `timeout_triggered` no counter
`LlmRetries` permite distinguir retries por timeout vs por erro HTTP no Grafana/OTel.

---

## Estendendo a plataforma

Os três pontos de extensão seguem o mesmo padrão de registry — implementar, registrar, ativar. Nenhum arquivo de core precisa ser alterado.

### Function Tool — chamável pelo LLM

```csharp
public class MinhasFuncoes
{
    [Description("Busca o preço atual de uma ação")]
    public async Task<string> ObterPreco(
        [Description("Ticker da ação, ex: PETR4")] string ticker)
        => JsonSerializer.Serialize(await _api.ObterPrecoAsync(ticker));
}

// Registrar no Program.cs:
registry.Register("obter_preco", AIFunctionFactory.Create(minhasFuncoes.ObterPreco));

// Ativar no agente:
// tools: [{ "type": "function", "name": "obter_preco" }]
```

### Middleware LLM — intercepta toda chamada ao LLM

```csharp
public class LoggingMiddleware : AgentMiddlewareBase
{
    public LoggingMiddleware(IChatClient inner, string agentId,
        Dictionary<string, string> settings, ILogger logger)
        : base(inner, agentId, settings, logger) { }

    protected override Task<ChatResponse> OnAfterResponseAsync(
        ChatResponse response, CancellationToken ct)
    {
        Logger.LogInformation("Agente {Id} respondeu: {Text}", AgentId, response.Text);
        return Task.FromResult(response);
    }
}

// Registrar no Program.cs:
registry.Register("Logging", (inner, agentId, settings, logger) =>
    new LoggingMiddleware(inner, agentId, settings, logger));

// Ativar no agente:
// middlewares: [{ "type": "Logging", "enabled": true }]
```

### Code Executor — nó C# puro em workflows Graph

```csharp
// Com tipagem (recomendado):
public class EnriquecerExecutor : ICodeExecutor<DadosBrutos, DadosEnriquecidos>
{
    public async Task<DadosEnriquecidos> ExecuteAsync(DadosBrutos input, CancellationToken ct)
        => await _servico.EnriquecerAsync(input, ct);
}

// Registrar no Program.cs:
registry.Register("enriquecer_dados", new EnriquecerExecutor());

// Referenciar no workflow:
// executors: [{ "id": "enriquecer-node", "functionName": "enriquecer_dados" }]
```

Veja o [CONTRIBUTING.md](CONTRIBUTING.md) para o guia completo passo a passo.

---

## Stack tecnológica

- .NET 8 · ASP.NET Core
- [Microsoft.Extensions.AI](https://github.com/dotnet/extensions) — abstrações de IA
- [Microsoft.Agents.AI.Workflows](https://github.com/microsoft/agents) — orquestração de workflows
- PostgreSQL 16 + Entity Framework Core 8 + Npgsql (raw SQL)
- Redis 7 (cache, rate limiting, SSE fan-out, Data Protection)
- React 19 + Vite + TypeScript + Tailwind CSS
- OpenTelemetry (tracing + métricas)

---

## Contribuindo

Contribuições de novas tools, middlewares e executores são bem-vindas. Veja o [CONTRIBUTING.md](CONTRIBUTING.md) para saber como adicionar cada tipo sem modificar arquivos do core.

Para bugs e solicitações de funcionalidades, [abra uma issue](../../issues).

---

## Licença

MIT — veja [LICENSE](LICENSE) para detalhes.
