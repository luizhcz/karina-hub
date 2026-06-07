// Tela "Fila" — saúde dos jobs standalone (background_response_jobs).
// KPIs: total, success rate, retry rate, p95 queue (latência fila→running),
// p95 total (ponta a ponta). Chart: created+failed dual-axis ao longo do
// período. Tabela: top workflows por carga com success rate e latências.
//
// Production-only é garantido no backend (background_response_jobs só recebe
// jobs reais — sandbox standalone passa pelo Chat Path, não por essa fila).

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getStandaloneJobSummary,
  getStandaloneJobTimeseries,
  type StandaloneJobByWorkflow,
  type StandaloneJobGranularity,
  type StandaloneJobTimeseriesBucket,
} from '../api/standaloneJobAnalytics'
import {
  formatInt,
  formatLatencyMs,
  formatPercent,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): StandaloneJobGranularity {
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

export function Fila() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getStandaloneJobSummary(projectId, fromIso, toIso, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  const timeseriesState = useApi(
    useCallback(
      (signal) => getStandaloneJobTimeseries(projectId, granularity, fromIso, toIso, { signal }),
      [projectId, granularity, fromIso, toIso],
    ),
    [projectId, granularity, fromIso, toIso],
    { skip },
  )

  if (!projectId) {
    return (
      <div className="flex flex-col gap-4">
        <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
        <Card>
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver a saúde da fila standalone." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data
  const successKnown = summary != null && (summary.completed + summary.failed) > 0

  const series: ChartSeries<StandaloneJobTimeseriesBucket>[] = [
    { dataKey: 'created', label: 'Criados', format: 'integer', yAxisId: 'left' },
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
              label="Jobs no período"
              value={summaryState.loading ? null : formatInt(summary?.totalJobs ?? 0)}
              hint={`${formatInt(summary?.completed ?? 0)} OK · ${formatInt(summary?.failed ?? 0)} falhas · ${formatInt(summary?.queued ?? 0)} fila`}
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
                    ? formatPercent(summary?.successRate ?? 0)
                    : '—'
              }
              hint={
                successKnown
                  ? `${formatInt(summary?.running ?? 0)} rodando · ${formatInt(summary?.cancelled ?? 0)} canceladas`
                  : 'Sem jobs resolvidos'
              }
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Taxa de retry"
              value={summaryState.loading ? null : formatPercent(summary?.retryRate ?? 0)}
              hint={`${formatInt(summary?.totalAttempts ?? 0)} tentativas no total`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="p95 fila → running"
              value={summaryState.loading ? null : formatLatencyMs(summary?.p95QueueMs ?? 0)}
              hint={`ponta a ponta p95: ${formatLatencyMs(summary?.p95TotalMs ?? 0)}`}
            />
          )}
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Criados e falhas ao longo do período"
          description="Eixo esquerdo: jobs criados. Eixo direito: falhas."
        />
        <div className="mt-4">
          {timeseriesState.error ? (
            <ErrorState error={timeseriesState.error} onRetry={timeseriesState.refetch} />
          ) : timeseriesState.loading ? (
            <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
          ) : (
            <TimeSeriesChart
              data={timeseriesState.data ?? []}
              series={series}
              granularity={granularity}
              dualAxis
              variant="line"
              height={260}
            />
          )}
        </div>
      </Card>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Workflows no período"
            description="Top 25 por carga. Ordene clicando no cabeçalho."
          />
        </div>
        {summaryState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          </div>
        ) : (
          <Table
            columns={workflowColumns}
            rows={summary?.workflows ?? null}
            loading={summaryState.loading}
            keyOf={(row) => row.workflowId}
            empty={{ title: 'Nenhum job no período' }}
          />
        )}
      </Card>
    </div>
  )
}

const workflowColumns: ReadonlyArray<Column<StandaloneJobByWorkflow>> = [
  {
    key: 'workflow',
    header: 'Workflow',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.workflowId}</span>,
    sortBy: (r) => r.workflowId,
  },
  {
    key: 'total',
    header: 'Total',
    align: 'right',
    cell: (r) => formatInt(r.total),
    sortBy: (r) => r.total,
  },
  {
    key: 'failed',
    header: 'Falhas',
    align: 'right',
    cell: (r) => (
      <span className={r.failed > 0 ? 'text-warning' : 'text-fg'}>{formatInt(r.failed)}</span>
    ),
    sortBy: (r) => r.failed,
  },
  {
    key: 'successRate',
    header: 'Success',
    align: 'right',
    cell: (r) => formatPercent(r.successRate),
    sortBy: (r) => r.successRate,
  },
  {
    key: 'attempts',
    header: 'Tentativas',
    align: 'right',
    cell: (r) => formatInt(r.totalAttempts),
    sortBy: (r) => r.totalAttempts,
  },
  {
    key: 'p95',
    header: 'p95 total',
    align: 'right',
    cell: (r) => formatLatencyMs(r.p95TotalMs),
    sortBy: (r) => r.p95TotalMs,
  },
]
