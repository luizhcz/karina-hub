// Tela "Roteador" — decisões de Router (intent + confidence).
// KPIs: total, distinct intents, avg confidence, ambiguity rate. Chart: total
// + falhas-de-decisão (low confidence + ambiguity) ao longo do período.
// BarChart: top intents por frequência. Tabela: estatísticas por intent.
//
// Sub-amostragem documentada: ~3% das decisões vêm com output não-parseável e
// são filtradas pelo backend. Total exibido = parseável (não bruto).

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { BarChart } from '../components/charts/BarChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getRouterDecisionSummary,
  getRouterDecisionTimeseries,
  type RouterDecisionTimeseriesBucket,
  type RouterGranularity,
  type RouterIntentStats,
} from '../api/routerDecisionAnalytics'
import {
  formatInt,
  formatPercent,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): RouterGranularity {
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

// Confiança em 0..1 com 2 decimais. formatPercent renderiza 1 decimal — pra
// "0.97" vira "97,0%" o que sumiria diferença sub-percentual entre intents.
// Aqui priorizamos 2 decimais.
function formatConfidence(value: number | null | undefined): string {
  if (value == null || !Number.isFinite(value)) return '—'
  return `${(value * 100).toFixed(1).replace('.', ',')}%`
}

export function Roteador() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getRouterDecisionSummary(projectId, fromIso, toIso, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  const timeseriesState = useApi(
    useCallback(
      (signal) => getRouterDecisionTimeseries(projectId, granularity, fromIso, toIso, { signal }),
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
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver decisões de Router." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data

  const series: ChartSeries<RouterDecisionTimeseriesBucket>[] = [
    { dataKey: 'total', label: 'Decisões', format: 'integer', yAxisId: 'left' },
    { dataKey: 'ambiguityCount', label: 'Ambíguas', format: 'integer', yAxisId: 'right' },
  ]

  // Top intents pro BarChart — só os primeiros 10 e ordenados por count DESC
  // (o backend já ordena, mas defensivo).
  const topIntents = (summary?.intents ?? []).slice(0, 10)

  return (
    <div className="flex flex-col gap-6">
      <FilterHeader range={range} onPresetChange={setPreset} onCustomChange={setCustom} />

      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Decisões no período"
              value={summaryState.loading ? null : formatInt(summary?.totalDecisions ?? 0)}
              hint="parseáveis (descarta ~3% malformado)"
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Intents distintos"
              value={summaryState.loading ? null : formatInt(summary?.distinctIntents ?? 0)}
              hint="cobertura do router"
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Confiança média"
              value={summaryState.loading ? null : formatConfidence(summary?.avgConfidence)}
              hint={`p50: ${formatConfidence(summary?.p50Confidence)}`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Taxa de ambiguidade"
              value={summaryState.loading ? null : formatPercent(summary?.ambiguityRate ?? 0)}
              hint={`${formatInt(summary?.ambiguityCount ?? 0)} fora-escopo/clarification/loop`}
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader
            title="Volume e ambiguidade ao longo do período"
            description="Eixo esquerdo: total de decisões. Eixo direito: ambíguas."
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

        <Card>
          <CardHeader
            title="Top intents"
            description="Frequência no período (top 10)."
          />
          <div className="mt-4">
            {summaryState.error ? (
              <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
            ) : summaryState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <BarChart
                data={topIntents}
                dataKey="count"
                labelKey="intent"
                format="integer"
                layout="horizontal"
                height={260}
                emptyTitle="Sem decisões no período"
              />
            )}
          </div>
        </Card>
      </div>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader
            title="Intents detalhados"
            description="Top 25 intents com confiança p50/p95/min. Atenção a min baixo — sinal de decisão fraca naquele intent."
          />
        </div>
        {summaryState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          </div>
        ) : (
          <Table
            columns={intentColumns}
            rows={summary?.intents ?? null}
            loading={summaryState.loading}
            keyOf={(row) => row.intent}
            empty={{ title: 'Nenhum intent no período' }}
          />
        )}
      </Card>
    </div>
  )
}

const intentColumns: ReadonlyArray<Column<RouterIntentStats>> = [
  {
    key: 'intent',
    header: 'Intent',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.intent}</span>,
    sortBy: (r) => r.intent,
  },
  {
    key: 'count',
    header: 'Decisões',
    align: 'right',
    cell: (r) => formatInt(r.count),
    sortBy: (r) => r.count,
  },
  {
    key: 'avg',
    header: 'Conf. média',
    align: 'right',
    cell: (r) => formatConfidence(r.avgConfidence),
    sortBy: (r) => r.avgConfidence,
  },
  {
    key: 'p50',
    header: 'p50',
    align: 'right',
    cell: (r) => formatConfidence(r.p50Confidence),
    sortBy: (r) => r.p50Confidence,
  },
  {
    key: 'p95',
    header: 'p95',
    align: 'right',
    cell: (r) => formatConfidence(r.p95Confidence),
    sortBy: (r) => r.p95Confidence,
  },
  {
    key: 'min',
    header: 'Mín',
    align: 'right',
    cell: (r) => (
      <span className={r.minConfidence < 0.5 ? 'text-warning' : 'text-fg'}>
        {formatConfidence(r.minConfidence)}
      </span>
    ),
    sortBy: (r) => r.minConfidence,
  },
]

