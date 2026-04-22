# Load test baselines

Relatórios HTML preservados como referência de "estado conhecido" de cada marco.

## Arquivos

| Arquivo | VUs × turnos | Fase / marco | Infra OK? | Turn1 | Turn2 | Notas |
|---|---|---|---|---|---|---|
| `burst-2turns-postfix.html` | 30 × 2 | Fase 1/2 (pré-dispatcher) | ✅ 0 exceções | 30/30 | 30/30 | Fix do lifecycle + OTel. Padrão "1 conn por subscriber". |
| `burst-100vu-dispatcher.html` | 100 × 2 | Fase 3 | ✅ 0 exceções | 100/100 | 99/100 | Primeira validação do PgNotifyDispatcher multiplexando LISTEN. |
| `burst-500vu-dispatcher.html` | 500 × 2 | Scale test | ✅ 0 exceções | 498/500 (99.6%) | 370/500 (74%) | **Infra aguenta.** 130 workflows terminam com `error` event — iteration limit do framework sob pressão de LLM. |
| `burst-1000vu-dispatcher.html` | 1000 × 2 | Scale test | ✅ 0 exceções | 941/1000 (94%) | 0/1000 | **Infra aguenta** (zero exceções, 0 429, 0 503, 0 pool issues). Limite encontrado é **aplicacional**: 100% dos turn2 recebem `error` event por exceder iteration limit do framework de workflow sob contenção severa de LLM. |

## Observação importante sobre os limites encontrados

**Infra (backend + PG + dispatcher) escala linearmente até pelo menos 1000 VUs concorrentes sem uma única exceção.** Todos os requests HTTP respondem 200, zero `NpgsqlOperationInProgressException`, zero pool exhausted, zero `back_pressure_503`, zero `429`.

**O limite observado a partir de 500 VUs é aplicacional, não de infra.** Workflow engine emite event `{"message":"Workflow encerrou sem atingir o nó final. Possível limite de iterações do framework."}` quando sob contenção do provedor LLM (OpenAI gpt-5.4-nano) o round converge devagar e bate max_rounds=3 do workflow `atendimento-cliente`.

Evidências:
- LLM responde em 1.2-1.4s em condições normais (30-100 VUs)
- Sob 500-1000 VUs concorrentes, `DurationMs` ainda aparece similar em amostras, mas framework de orquestração (Microsoft.Agents.AI) aborta workflows
- Postgres `max_connections=500`, backend pools gen=350 + sse=10 + reporting=20 — folga grande
- `ChatMaxConcurrentExecutions=1500` — não é gargalo
- `PgNotifyDispatcher` multiplexando 1000+ subscribers em 1 conn PG → perfeito

**Próximos passos possíveis** (fora do escopo atual):
- Aumentar `max_rounds` do workflow `atendimento-cliente` (muda comportamento do produto)
- Investigar se há workarounds específicos do Microsoft.Agents.AI para LLMs lentos sob carga
- Usar modelo mais rápido/maior capacidade (gpt-5.4-mini não-nano, ou local)
- Rate limit explícito no backend para não bombardear LLM (limite de workflows concorrentes por provider)

## Como atualizar

Sempre que uma mudança arquitetural afetar o comportamento do load test, salvar novo baseline antes de commitar:

```bash
# Cenário: atualmente usamos 30, 100, 500, 1000 VUs como marcos
VUS=<N> ./scripts/load/run_burst.sh
cp scripts/load/last-report.html \
   scripts/load/baselines/burst-<N>vu-<marco>.html
```

## O que NÃO fazer

- Não commitar `last-report.html` diretamente (gitignored) — é sempre regenerado pelo próximo run.
- Não substituir baselines antigos sem motivo — eles documentam história de capacidade.

## Parâmetros de teste

| Parâmetro | Valor padrão | Env var |
|---|---|---|
| VUs | 30 | `VUS` |
| Workflow | `atendimento-cliente` | `WORKFLOW_ID` |
| Turnos por VU | 2 (fixo) | — |
| Mensagem 1 | "Olá, queria ver minha carteira" | `USER_MESSAGE_1` |
| Mensagem 2 | "E qual a minha melhor posição atual?" | `USER_MESSAGE_2` |

Rodar com: `VUS=500 ./scripts/load/run_burst.sh`
