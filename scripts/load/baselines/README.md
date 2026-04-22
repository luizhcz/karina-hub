# Load test baselines

Relatórios HTML preservados como referência de "estado conhecido bom" após fixes estruturais. Cada arquivo aqui é um snapshot de `scripts/load/last-report.html` copiado após um run considerado canônico.

## Arquivos

| Arquivo | Cenário | Commit de referência | Notas |
|---|---|---|---|
| `burst-2turns-postfix.html` | 30 VUs × 2 turnos (atendimento-cliente) | `20472a6`, `3c23654` | Primeiro run verde após fix do lifecycle do PgEventBus. `turn1_success=30/30`, `turn2_success=30/30`, `http_req_failed=0%`, `workflow_completion p95 <5s`. Arquitetura 1-conn-por-subscriber. |
| `burst-100vu-dispatcher.html` | 100 VUs × 2 turnos (atendimento-cliente) | Fase 3 | Primeiro run verde com `PgNotifyDispatcher` singleton multiplexando LISTEN. `turn1_success=100/100`, `turn2_success=99/100`, `http_req_failed=0%`, `workflow_completion p95 ~4s`, `conversation_total p95 ~8s`. Teto estrutural subiu de ~50 para 1000+ subscribers por instância. |

## Como atualizar

Sempre que uma mudança arquitetural afetar o comportamento do load test (ex: Fase 3 — PgNotifyDispatcher), salvar novo baseline antes de commitar:

```bash
./scripts/load/run_burst.sh
cp scripts/load/last-report.html \
   scripts/load/baselines/<nome-descritivo>.html
```

Naming: `<cenário>-<marco>.html` (ex: `burst-2turns-dispatcher.html`, `steady-100vu-postfase3.html`).

## O que NÃO fazer

- Não commitar `last-report.html` diretamente (gitignored) — é sempre regenerado pelo próximo run.
- Não substituir baselines antigos sem motivo — eles documentam história de capacidade. Adicionar novo arquivo é barato; sobrescrever perde contexto.
