// Tela "Webhooks" — entrega de callbacks de webhooks (webhook_deliveries).
// KPIs: total, delivery rate, p95 latência, tentativas em voo. Chart: created
// + delivered + failed ao longo do período. BarChart: breakdown por classe
// HTTP (2xx/4xx/5xx/no-response). Tabela: top hosts.

import { useCallback, useMemo } from 'react'
import { Card, CardHeader, ErrorState, Stat, Table, type Column } from '../components/ui'
import { TimeSeriesChart, type ChartSeries } from '../components/charts/TimeSeriesChart'
import { BarChart } from '../components/charts/BarChart'
import { FilterHeader } from '../components/FilterHeader'
import { useApi } from '../hooks/useApi'
import { useDateRange } from '../hooks/useDateRange'
import { useIdentity } from '../stores/identity'
import {
  getWebhookSummary,
  getWebhookTimeseries,
  type WebhookDeliveryTimeseriesBucket,
  type WebhookGranularity,
  type WebhookHostRow,
} from '../api/webhookDeliveryAnalytics'
import {
  formatInt,
  formatLatencyMs,
  formatPercent,
  toIsoUtc,
} from '../utils/format'

function pickGranularity(from: Date, to: Date): WebhookGranularity {
  const hours = (to.getTime() - from.getTime()) / 3600_000
  return hours <= 72 ? 'hour' : 'day'
}

export function Webhooks() {
  const identity = useIdentity()
  const { range, setPreset, setCustom } = useDateRange()
  const projectId = identity?.projectId ?? ''
  const fromIso = useMemo(() => toIsoUtc(range.from), [range.from])
  const toIso = useMemo(() => toIsoUtc(range.to), [range.to])
  const granularity = useMemo(() => pickGranularity(range.from, range.to), [range])

  const skip = !projectId

  const summaryState = useApi(
    useCallback(
      (signal) => getWebhookSummary(projectId, fromIso, toIso, { signal }),
      [projectId, fromIso, toIso],
    ),
    [projectId, fromIso, toIso],
    { skip },
  )

  const timeseriesState = useApi(
    useCallback(
      (signal) => getWebhookTimeseries(projectId, granularity, fromIso, toIso, { signal }),
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
          <CardHeader title="Selecione um projeto" description="Use o seletor acima pra ver entregas de webhook." />
        </Card>
      </div>
    )
  }

  const summary = summaryState.data
  const deliveredKnown = summary != null && (summary.delivered + summary.failed) > 0

  const series: ChartSeries<WebhookDeliveryTimeseriesBucket>[] = [
    { dataKey: 'delivered', label: 'Entregues', format: 'integer', yAxisId: 'left' },
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
              label="Tentativas no período"
              value={summaryState.loading ? null : formatInt(summary?.totalDeliveries ?? 0)}
              hint={`${formatInt(summary?.delivered ?? 0)} OK · ${formatInt(summary?.failed ?? 0)} falhas`}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Delivery rate"
              value={
                summaryState.loading
                  ? null
                  : deliveredKnown
                    ? formatPercent(summary?.deliveryRate ?? 0)
                    : '—'
              }
              hint={deliveredKnown ? 'sobre tentativas resolvidas' : 'Sem entregas no período'}
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="p95 entrega"
              value={summaryState.loading ? null : formatLatencyMs(summary?.p95DeliveryMs ?? 0)}
              hint="CreatedAt → DeliveredAt"
            />
          )}
        </Card>
        <Card>
          {summaryState.error ? (
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          ) : (
            <Stat
              label="Em voo"
              value={summaryState.loading ? null : formatInt((summary?.pending ?? 0) + (summary?.delivering ?? 0))}
              hint={`${formatInt(summary?.pending ?? 0)} pending · ${formatInt(summary?.delivering ?? 0)} delivering`}
            />
          )}
        </Card>
      </div>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <Card className="xl:col-span-2">
          <CardHeader
            title="Entregas e falhas ao longo do período"
            description="Eixo esquerdo: entregues. Eixo direito: falhas."
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
            title="Status HTTP do receiver"
            description="Classe da resposta. 'no-response' = timeout/DNS/connection."
          />
          <div className="mt-4">
            {summaryState.error ? (
              <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
            ) : summaryState.loading ? (
              <div className="h-60 animate-pulse rounded-lg bg-surface-hover" />
            ) : (
              <BarChart
                data={summary?.statusCodes ?? []}
                dataKey="count"
                labelKey="bucket"
                format="integer"
                layout="horizontal"
                height={260}
                emptyTitle="Sem entregas no período"
              />
            )}
          </div>
        </Card>
      </div>

      <Card padded={false}>
        <div className="p-5">
          <CardHeader title="Hosts" description="Top 20 destinos por tentativas, com taxa de entrega." />
        </div>
        {summaryState.error ? (
          <div className="px-5 pb-5">
            <ErrorState error={summaryState.error} onRetry={summaryState.refetch} />
          </div>
        ) : (
          <Table
            columns={hostColumns}
            rows={summary?.hosts ?? null}
            loading={summaryState.loading}
            keyOf={(row) => row.host}
            empty={{ title: 'Nenhum host no período' }}
          />
        )}
      </Card>
    </div>
  )
}

const hostColumns: ReadonlyArray<Column<WebhookHostRow>> = [
  {
    key: 'host',
    header: 'Host',
    cell: (r) => <span className="font-mono text-xs text-fg">{r.host}</span>,
    sortBy: (r) => r.host,
  },
  {
    key: 'total',
    header: 'Tentativas',
    align: 'right',
    cell: (r) => formatInt(r.total),
    sortBy: (r) => r.total,
  },
  {
    key: 'delivered',
    header: 'Entregues',
    align: 'right',
    cell: (r) => formatInt(r.delivered),
    sortBy: (r) => r.delivered,
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
    key: 'deliveryRate',
    header: 'Taxa',
    align: 'right',
    cell: (r) => formatPercent(r.deliveryRate),
    sortBy: (r) => r.deliveryRate,
  },
]
