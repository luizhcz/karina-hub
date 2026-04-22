# Load tests

Testes de carga do chat EfsAiHub. Usam [k6](https://k6.io/) disparando contra a API rodando em `docker compose`.

## Cenário disponível

### `atendimento_cliente_burst.js` — burst puro de 30 conexões

Simula 30 clientes abrindo o chat **ao mesmo tempo**, enviando uma mensagem cada, e aguardando a resposta completa via SSE. Workflow alvo: `atendimento-cliente`.

- 30 VUs iniciam simultaneamente (`startTime: 0s`, `per-vu-iterations: 1`)
- Cada VU usa `userId` único (`load-vu{N}-{timestamp}`) — evita rate-limit cross-VU
- LLM **real** (gpt-5.4-mini via Azure/OpenAI) — custo estimado US$ 0,10–0,20 por rodada
- Teto de espera por conversa: 120s

## Prereqs

```bash
brew install k6
docker compose up -d --build
curl -sf http://localhost:5189/health/ready   # 200 OK
```

**MCP findsmart** (`host.docker.internal:8000`): opcional. O agente `atendimento-agent-cliente` inclui a tool `search_asset` mas sabe responder saudação básica sem ela. Se não estiver up, aparece warning no log do backend — ignorável para este teste.

**Seed do workflow**: `atendimento-cliente` precisa estar no DB. Se `POST /api/conversations` retorna 404 ou `workflow not found`, rode o seed:
```bash
docker compose exec -T postgres psql -U efs_ai_hub -d efs_ai_hub < db/seed_default_project.sql
```

## Rodar

```bash
./scripts/load/run_burst.sh
```

O runner:
1. Valida `/health/ready`
2. Limpa chaves `efs-ai-hub:rl:chat:load-*` do Redis (isola runs)
3. Captura snapshot de conexões PG antes/depois (detecta leak)
4. Roda `k6 run` com o script
5. Agrega logs relevantes do backend
6. Gera relatório HTML em `scripts/load/last-report.html`

Saída termina com código != 0 se qualquer threshold k6 falhar.

### Observability em paralelo (opcional)

Em outro terminal, enquanto o teste roda:

```bash
./scripts/load/monitor.sh
```

Imprime a cada 2s: estado do pool PG, memória Redis, contagem de chaves `efs-ai-hub:*`. Útil para ver contenção em tempo real.

## Variáveis de ambiente

| Var | Default | Descrição |
|---|---|---|
| `BASE_URL` | `http://localhost:5189` | Endpoint do backend |
| `WORKFLOW_ID` | `atendimento-cliente` | Workflow alvo |
| `USER_MESSAGE` | `Olá, queria ver minha carteira` | Mensagem enviada por cada VU |
| `POSTGRES_USER` | `efs_ai_hub` | Usado apenas pelo runner para snapshot de conexões |
| `POSTGRES_DB` | `efs_ai_hub` | Idem |

Exemplo:
```bash
BASE_URL=http://localhost:5189 USER_MESSAGE="Qual minha posição?" ./scripts/load/run_burst.sh
```

## Interpretação dos thresholds

| Métrica | Threshold | Significado |
|---|---|---|
| `http_req_failed` | `rate<0.05` | Menos de 5% das requests HTTP falharam. Acima disso = backend instável. |
| `checks` | `rate>0.95` | Mais de 95% das asserções passaram (conversa criada, mensagem aceita, SSE fechou com `workflow_completed`). |
| `workflow_completion_ms` | `p(95)<45000` | 95% das conversas completam em menos de 45s. Acima disso, provavelmente LLM é o gargalo. |
| `ttfb_create_conversation` | `p(95)<2000` | 95% das criações de conversa respondem em <2s. Acima = DB ou middleware está lento. |
| `rate_limited_429` | `count<2` | Menos de 2 respostas 429 no total. Se disparar muito, rate limit foi atingido. |
| `back_pressure_503` | `count==0` | Zero rejeições de slot no `ChatExecutionRegistry`. Se > 0, `ChatMaxConcurrentExecutions` está sub-dimensionado. |

## Troubleshooting

### `rate_limited_429 > 0`

Rate limit de chat ou de projeto foi atingido. Temporariamente, em `src/EfsAiHub.Host.Api/appsettings.Development.json`:
```json
{
  "ChatRateLimit": {
    "MaxMessages": 100,
    "WindowSeconds": 60,
    "MaxMessagesPerConversation": 50
  }
}
```
Reinicie o backend: `docker compose restart backend`.

### `back_pressure_503 > 0`

Aumente `ChatMaxConcurrentExecutions` em `appsettings.Development.json`:
```json
{
  "WorkflowEngine": { "ChatMaxConcurrentExecutions": 500 }
}
```

### `workflow_completion_ms p95 > 45000`

Gargalo provável é o LLM. Caminhos para investigar:
- Ver spans `LlmCall` em OpenTelemetry (se `OpenTelemetry:OtlpEndpoint` configurado)
- `docker compose logs backend | grep -i "llm\|openai\|azure"` — procurar por retries ou circuit breaker abrindo
- Se executando em rede lenta, o provider remoto é o limite — não adianta tunar backend

### `http_req_failed > 5%`

Ver logs do backend:
```bash
docker compose logs --tail=500 backend | grep -E "ERROR|Exception|fail"
```

Causas comuns:
- Workflow `atendimento-cliente` não seedado (404 no POST /conversations)
- MCP findsmart obrigatório e offline (se agente foi ajustado pra falhar sem MCP)
- Postgres pool exaurido (raro com 30 VUs — veria `PostgresException: timeout`)

### Conexões PG não voltam ao baseline após o teste

Possível leak de `IDbContextFactory`. Rode novamente o `run_burst.sh` uma segunda vez e confira — se o número cresce a cada run, há leak real. Em dev isolado não é bloqueante; reportar como issue.

## Ferramenta vs. não-ferramenta

Este teste é **smoke test de capacidade** — valida que 30 conexões simultâneas de usuários reais não quebram o chat. **Não substitui**:
- Testes unitários (`dotnet test`)
- Testes de integração (`tests/EfsAiHub.Tests.Integration`)
- Benchmark de throughput sustentado (precisaria cenário `steady-long-turns` — ver backlog LOAD-2)

## Backlog

- **LOAD-1** — Portar para xk6/SSE para medir TTFE separado de TTC.
- **LOAD-2** — Cenário `steady-long-turns` (30 VUs × 5 turnos × 5 min).
- **LOAD-3** — Integrar ao CI (nightly) com thresholds apertados após baseline.
