// Tela "Confiabilidade" — saúde de execuções de workflows por projeto.
// Tripé clássico SRE: success rate, latência, breakdown de falhas. Reusa o
// FilterHeader, KPI Cards, TimeSeriesChart (dual-axis), BarChart (primeira
// tela que finalmente pluga o BarChart entregue no scaffold inicial).
//
// Production-only: backend joina com v_production_executions — sandbox NÃO
// entra no cálculo. Métrica confiável pra ops.

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { BarChart } from '../components/charts/BarChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getExecutionFailureBreakdown,
  getExecutionSummary,
  getExecutionTimeseries,
  type ExecutionGranularity,
  type ExecutionTimeseriesBucket,
} from '../api/executionAnalytics'
import {
  formatInt,
  formatLatencyMs,
  formatPercentPoints,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): ExecutionGranularity {
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

export function Confiabilidade() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getExecutionSummary(projectId, fromIso, toIso, undefined, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  const timeseriesState = useApi(
    useCallback(
      (signal) => getExecutionTimeseries(projectId, granularity, fromIso, toIso, undefined, { signal }),
      [projectId, granularity, fromIso, toIso],
    ),
    [projectId, granularity, fromIso, toIso],
    { skip },
  )

  const failuresState = useApi(
    useCallback(
      (signal) => getExecutionFailureBreakdown(projectId, fromIso, toIso, undefined, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver a saúde das execuções." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data

  // Backend devolve successRate em percent points (0..100). Quando ainda não
  // houve execução resolvida o valor vem 0 — distinguimos "0 conhecido" de
  // "sem dado" só por total + completed + failed, igual a tela de Ferramentas.
  const successKnown = summary != null && (summary.completed + summary.failed) > 0

  const timeseriesSeries: ChartSeries<ExecutionTimeseriesBucket>[] = [
    { dataKey: 'total', label: 'Total', format: 'integer', yAxisId: 'left' },
    { dataKey: 'failed', label: 'Falhas', format: 'integer', yAxisId: 'right' },
  ]

  return (
    <div className="flex flex-col gap-6">
      <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Execuções no período"
              value={summaryState.loading ? null : formatInt(summary?.total ?? 0)}
              hint={`${formatInt(summary?.completed ?? 0)} concluídas · ${formatInt(summary?.failed ?? 0)} falhas`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Success rate"
              value={
                summaryState.loading
                  ? null
                  : successKnown
                    ? formatPercentPoints(summary?.successRate ?? 0)
                    : '—'
              }
              hint={
                successKnown
                  ? `${formatInt(summary?.cancelled ?? 0)} canceladas · ${formatInt(summary?.running ?? 0)} rodando`
                  : 'Sem execuções resolvidas'
              }
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="p50 latência"
              value={summaryState.loading ? null : formatLatencyMs(summary?.p50Ms ?? 0)}
              hint="execuções concluídas"
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="p95 latência"
              value={summaryState.loading ? null : formatLatencyMs(summary?.p95Ms ?? 0)}
              hint="execuções concluídas"
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader
            title="Volume e falhas ao longo do período"
            description="Eixo esquerdo: total de execuções. Eixo direito: apenas falhas."
          />
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
                variant="line"
                height={260}
              />
            )}
          </div>
        </Card>

        <Card>
          <CardHeader
            title="Falhas por categoria"
            description="Espelha a tag error.category das métricas Prometheus."
          />
          <div className="mt-4">
            {failuresState.error ? (
              <ErrorState error={failuresState.error} onRetry={failuresState.refetch} />
            ) : failuresState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <BarChart
                data={failuresState.data ?? []}
                dataKey="count"
                labelKey="category"
                format="integer"
                layout="horizontal"
                height={260}
                color="rgb(var(--color-warning))"
                emptyTitle="Sem falhas no período"
              />
            )}
          </div>
        </Card>
      </div>
    </div>
  )
}
