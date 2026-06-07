// Visão geral do projeto: 4 KPIs + chart custo/tokens + top agents + budget.
// Todo dado vem de /analytics/projects/{id}/{overview,timeseries,agents,budget}.
// Cada card faz seu próprio fetch via useApi pra que parte da tela renderize
// mesmo se um endpoint falhar — sem cascade de loading global.

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { Gauge } from '../components/charts/Gauge'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getProjectAgents,
  getProjectBudget,
  getProjectOverview,
  getProjectTimeseries,
  type ProjectAgentBreakdown,
  type ProjectTimeseriesBucket,
  type Granularity,
} from '../api/analytics'
import {
  formatCurrency,
  formatInt,
  formatLatencyMs,
  formatPercent,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): Granularity {
  // Heurística: até 3 dias usa hora pra capturar pico horário; acima vira dia
  // pra que o eixo X não vire ilegível.
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

export function Overview() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  // skip enquanto projectId não estiver populado — sem isso, o fetch dispara
  // com path `/analytics/projects//…` e devolve 404 até o useAutoSelectProject
  // do Layout escolher um projeto (ou o user clicar manualmente).
  const skip = !projectId

  const overviewState = useApi(
    useCallback(
      (signal) => getProjectOverview(projectId, fromIso, toIso, false, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )
  const timeseriesState = useApi(
    useCallback(
      (signal) => getProjectTimeseries(projectId, granularity, fromIso, toIso, undefined, false, { signal }),
      [projectId, fromIso, toIso, granularity],
    ),
    [projectId, fromIso, toIso, granularity],
    { skip },
  )
  const agentsState = useApi(
    useCallback(
      (signal) => getProjectAgents(projectId, 10, fromIso, toIso, false, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )
  const budgetState = useApi(
    useCallback((signal) => getProjectBudget(projectId, { signal }), [projectId]),
    [projectId],
    { skip },
  )

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver os indicadores." />
        </Card>
      </div>
    )
  }

  const overview = overviewState.data
  const errorRate = overview && overview.totalExecutions > 0
    ? overview.failed / overview.totalExecutions
    : null

  const timeseriesSeries: ChartSeries<ProjectTimeseriesBucket>[] = [
    { dataKey: 'costUsd', label: 'Custo (USD)', format: 'currency', yAxisId: 'left' },
    { dataKey: 'tokens', label: 'Tokens', format: 'integer', yAxisId: 'right' },
  ]

  return (
    <div className="flex flex-col gap-6">
      <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Card>
          {overviewState.error ? (
            <ErrorState error={overviewState.error} onRetry={overviewState.refetch} />
          ) : (
            <Stat
              label="Custo no período"
              value={overviewState.loading ? null : formatCurrency(overview?.totalCostUsd ?? 0)}
              hint="USD acumulado"
            />
          )}
        </Card>
        <Card>
          {overviewState.error ? (
            <ErrorState error={overviewState.error} onRetry={overviewState.refetch} />
          ) : (
            <Stat
              label="Tokens"
              value={overviewState.loading ? null : formatInt(overview?.totalTokens ?? 0)}
              hint={`${formatInt(overview?.totalCalls ?? 0)} chamadas`}
            />
          )}
        </Card>
        <Card>
          {overviewState.error ? (
            <ErrorState error={overviewState.error} onRetry={overviewState.refetch} />
          ) : (
            <Stat
              label="Execuções"
              value={overviewState.loading ? null : formatInt(overview?.totalExecutions ?? 0)}
              hint={`${formatInt(overview?.completed ?? 0)} concluídas`}
            />
          )}
        </Card>
        <Card>
          {overviewState.error ? (
            <ErrorState error={overviewState.error} onRetry={overviewState.refetch} />
          ) : (
            <Stat
              label="Taxa de erro"
              // Null sem execuções: distinto de "0% conhecido" — explica via hint.
              value={
                overviewState.loading
                  ? null
                  : errorRate == null
                    ? '—'
                    : formatPercent(errorRate)
              }
              hint={errorRate == null ? 'Sem execuções no período' : `${formatInt(overview?.failed ?? 0)} falhas`}
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader title="Custo e tokens" description="Série temporal agregada por bucket." />
          <div className="mt-4">
            {timeseriesState.error ? (
              <ErrorState error={timeseriesState.error} onRetry={timeseriesState.refetch} />
            ) : timeseriesState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <TimeSeriesChart
                data={timeseriesState.data ?? []}
                series={timeseriesSeries}
                granularity={granularity}
                dualAxis
                variant="area"
                height={260}
              />
            )}
          </div>
        </Card>

        <Card>
          <CardHeader title="Orçamento diário" description="Aviso quando ultrapassa o limite do projeto." />
          <div className="mt-4 flex flex-col items-center gap-4">
            {budgetState.error ? (
              <ErrorState error={budgetState.error} onRetry={budgetState.refetch} />
            ) : budgetState.loading ? (
              <div className="h-32 w-40 animate-pulse rounded-lg bg-surface-hover" />
            ) : budgetState.data?.maxCostUsdPerDay == null ? (
              <p className="px-4 py-6 text-center text-sm text-fg-muted">
                Sem limite de custo diário configurado neste projeto.
              </p>
            ) : (
              <Gauge
                value={budgetState.data.costUsagePct ?? 0}
                label={`${formatCurrency(budgetState.data.todayCostUsd)} hoje`}
                caption={`Limite ${formatCurrency(budgetState.data.maxCostUsdPerDay)} / dia`}
              />
            )}
          </div>
        </Card>
      </div>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Agentes com maior custo"
            description="Top 10 do período. Ordene clicando no cabeçalho."
          />
        </div>
        {agentsState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={agentsState.error} onRetry={agentsState.refetch} />
          </div>
        ) : (
          <Table
            columns={overviewAgentColumns}
            rows={agentsState.data}
            loading={agentsState.loading}
            keyOf={(row) => row.agentId}
            empty={{ title: 'Nenhum agente no período' }}
          />
        )}
      </Card>
    </div>
  )
}

const overviewAgentColumns: ReadonlyArray<Column<ProjectAgentBreakdown>> = [
  {
    key: 'agent',
    header: 'Agente',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-medium text-fg">{r.agentName ?? r.agentId}</span>
        {r.modelId && <span className="text-xs text-fg-dim">{r.modelId}</span>}
      </div>
    ),
    sortBy: (r) => r.agentName ?? r.agentId,
  },
  {
    key: 'calls',
    header: 'Chamadas',
    align: 'right',
    cell: (r) => formatInt(r.calls),
    sortBy: (r) => r.calls,
  },
  {
    key: 'tokens',
    header: 'Tokens',
    align: 'right',
    cell: (r) => formatInt(r.totalTokens),
    sortBy: (r) => r.totalTokens,
  },
  {
    key: 'cost',
    header: 'Custo',
    align: 'right',
    cell: (r) => formatCurrency(r.costUsd),
    sortBy: (r) => r.costUsd,
  },
  {
    key: 'p95',
    header: 'p95 latência',
    align: 'right',
    cell: (r) => formatLatencyMs(r.p95DurationMs),
    sortBy: (r) => r.p95DurationMs,
  },
  {
    key: 'errorRate',
    header: 'Erro',
    align: 'right',
    cell: (r) => formatPercent(r.errorRate),
    sortBy: (r) => r.errorRate,
  },
]
