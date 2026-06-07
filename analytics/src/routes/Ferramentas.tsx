// Tela "Ferramentas" — chamada de ferramenta (GenericTool) por projeto.
// V1: resumo + série temporal + tabela por tool (sem drill-down de
// arguments/result — esses podem conter PII, decisão pra V2).
//
// 4 KPIs: chamadas totais, tools distintas, taxa de erro, p95 latência.
// Chart dual-axis: calls (left) + falhas (right) ao longo do período.
// Tabela: top tools com p50/p95/max latência + taxa de erro por tool.

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getToolSummary,
  getToolTimeseries,
  type ToolTimeseriesBucket,
  type ToolUsageRow,
  type ToolGranularity,
} from '../api/toolAnalytics'
import {
  formatInt,
  formatLatencyMs,
  formatPercent,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): ToolGranularity {
  // Mesma heurística do Overview de custo: até 3 dias usa hora; acima vira
  // dia pra que o eixo X não vire ilegível.
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

export function Ferramentas() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getToolSummary(projectId, fromIso, toIso, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )
  const timeseriesState = useApi(
    useCallback(
      (signal) => getToolTimeseries(projectId, granularity, fromIso, toIso, { signal }),
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
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver o uso de ferramentas." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data
  const errorRate = summary && (summary.totalSucceeded + summary.totalFailed) > 0
    ? summary.errorRate
    : null

  const timeseriesSeries: ChartSeries<ToolTimeseriesBucket>[] = [
    { dataKey: 'calls', label: 'Chamadas', format: 'integer', yAxisId: 'left' },
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
              label="Chamadas no período"
              value={summaryState.loading ? null : formatInt(summary?.totalCalls ?? 0)}
              hint={`${formatInt(summary?.totalSucceeded ?? 0)} sucesso · ${formatInt(summary?.totalFailed ?? 0)} falha`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Tools distintas"
              value={summaryState.loading ? null : formatInt(summary?.distinctTools ?? 0)}
              hint="invocadas no período"
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Taxa de erro"
              // Null sem chamadas — distinto de "0% conhecido"; explica via hint.
              value={
                summaryState.loading
                  ? null
                  : errorRate == null
                    ? '—'
                    : formatPercent(errorRate)
              }
              hint={errorRate == null ? 'Sem chamadas no período' : `${formatInt(summary?.totalFailed ?? 0)} falhas`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="p95 latência"
              value={summaryState.loading ? null : formatLatencyMs(summary?.p95DurationMs ?? 0)}
              hint="agregado, todas as tools"
            />
          )}
        </Card>
      </div>

      <Card>
        <CardHeader
          title="Volume e falhas"
          description="Chamadas por bucket (eixo esquerdo) e falhas (eixo direito) ao longo do período."
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

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Tools no período"
            description="Ordene clicando no cabeçalho. p50/p95 calculados sobre todas as chamadas do período."
          />
        </div>
        {summaryState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          </div>
        ) : (
          <Table
            columns={toolColumns}
            rows={summary?.tools ?? null}
            loading={summaryState.loading}
            keyOf={(row) => row.toolName}
            empty={{ title: 'Nenhuma chamada de tool no período' }}
          />
        )}
      </Card>
    </div>
  )
}

const toolColumns: ReadonlyArray<Column<ToolUsageRow>> = [
  {
    key: 'tool',
    header: 'Tool',
    cell: (r) => (
      <div className="flex flex-col">
        <span className="font-medium text-fg">{r.toolName}</span>
        <span className="text-xs text-fg-dim">
          {formatInt(r.distinctAgents)} agente{r.distinctAgents === 1 ? '' : 's'}
        </span>
      </div>
    ),
    sortBy: (r) => r.toolName,
  },
  {
    key: 'calls',
    header: 'Chamadas',
    align: 'right',
    cell: (r) => formatInt(r.calls),
    sortBy: (r) => r.calls,
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
    key: 'errorRate',
    header: 'Erro',
    align: 'right',
    cell: (r) => formatPercent(r.errorRate),
    sortBy: (r) => r.errorRate,
  },
  {
    key: 'p50',
    header: 'p50',
    align: 'right',
    cell: (r) => formatLatencyMs(r.p50DurationMs),
    sortBy: (r) => r.p50DurationMs,
  },
  {
    key: 'p95',
    header: 'p95',
    align: 'right',
    cell: (r) => formatLatencyMs(r.p95DurationMs),
    sortBy: (r) => r.p95DurationMs,
  },
  {
    key: 'max',
    header: 'Máx',
    align: 'right',
    cell: (r) => formatLatencyMs(r.maxDurationMs),
    sortBy: (r) => r.maxDurationMs,
  },
]
