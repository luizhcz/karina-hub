# EFS AI Hub · Analytics

Dashboard de Custo & Token Usage (V1) — projeto Vite separado, irmão do `mvp/`.

## Como rodar

```bash
npm install
npm run dev          # http://localhost:5174
npm run build        # bundle de produção em dist/
npm run preview      # serve o dist/
npm run typecheck    # tsc sem emit
```

Por padrão o Vite faz proxy de `/api/aihub` → `http://localhost:5189` (backend
Kestrel). Suba o backend antes (ver README raiz do repo).

## Variáveis de ambiente

Crie `.env.local` na raiz de `analytics/` quando precisar:

| Var | Quando setar | Default |
|---|---|---|
| `VITE_API_HOST` | quando o backend não está atrás do mesmo origin (ex.: dev contra deploy remoto) | proxy local em `5189` |
| `VITE_DEV_PERMISSIONS` | dev local sem proxy IdP — injeta permissions CSV no header | vazio (autenticado sem permissions) |

Em dev, popular `localStorage['efs-analytics-identity']` com
`{"account":"<id>","name":"<nome>","userType":"cliente","permissions":[],"projectId":"<id>","projectName":"<nome>"}`
é a forma mais rápida de simular sessão sem o proxy.

## Backend esperado

- `http://localhost:5189` (Kestrel container do EFS AI Hub) servindo
  `/api/aihub/*`. Endpoints consumidos:
  - `GET /me`
  - `GET /analytics/projects/{id}/{overview,timeseries,agents,budget}`
  - `GET /token-usage/{throughput,workflows/summary,projects/summary}`

## Decisões (5 linhas)

1. **`recharts`** como lib de charts — declarativo, idiomático em React, ~150 KB
   gzip isolado em chunk `vendor-charts`, sem dep de imperative DOM API (vs.
   Chart.js) e sem custom DSL (vs. Visx/Plot). Tradeoff: tema custom requer
   tokens via CSS vars, o que já é nosso padrão.
2. **Sem React Query / SWR / Zustand** — mesma decisão consciente do MVP. `useApi`
   centraliza AbortController + retry exponencial em 5xx; isso cobre o que o V1
   precisa sem trazer 50 KB de runtime e modelo mental extra.
3. **Sem mock data** — todo estado loading/erro/empty vem do contrato real do
   backend; renderização inválida prefere `—` e empty states explícitos.
4. **`strict: true` + `noUnusedLocals/Parameters`** — alinha com o tsconfig do
   MVP; tipos dos DTOs espelham os controllers (referenciados em comentário).
5. **Componentes reusáveis em 3 categorias** (`ui/`, `charts/`, `filters/`) com
   header obrigatório descrevendo contrato — quando vier Reliability/LLM Calls,
   `Stat`, `Table`, `TimeSeriesChart`, `BarChart`, `Gauge` reusam sem rewrite.
