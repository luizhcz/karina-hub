import { useState } from 'react'
import { Link } from 'react-router'
import { useExecutionSummary, useExecutionTimeseries } from '../../api/analytics'
import { useTokenSummary } from '../../api/token-usage'
import { Card } from '../../shared/ui/Card'
import { MetricCard } from '../../shared/data/MetricCard'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { TimeseriesChart } from '../../shared/charts/TimeseriesChart'
import { GaugeChart } from '../../shared/charts/GaugeChart'
import { DonutChart } from '../../shared/charts/DonutChart'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { Button } from '../../shared/ui/Button'
import { formatDuration, formatNumber } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'

export function MetricsOverviewPage() {
  const [range, setRange] = useState<TimeRange>('24h')
  const from = getFromDate(range)

  const summary = useExecutionSummary({ from })
  const timeseries = useExecutionTimeseries({ from })
  const tokens = useTokenSummary({ from })

  if (summary.isLoading) return <PageLoader />
  if (summary.error) return <ErrorCard message="Erro ao carregar métricas" onRetry={summary.refetch} />

  const s = summary.data
  const t = tokens.data
  const buckets = (timeseries.data?.buckets ?? []).map((b) => ({
    ...b,
    bucket: b.bucket.slice(11, 16),
  }))

  const failRate = s ? (s.total > 0 ? ((s.failed / s.total) * 100).toFixed(1) : '0') : '—'

  const donutData = s
    ? [
        { name: 'Completed', value: s.completed, color: '#10B981' },
        { name: 'Failed', value: s.failed, color: '#EF4444' },
        { name: 'Running', value: s.running, color: '#3B82F6' },
        { name: 'Pending', value: s.pending, color: '#F59E0B' },
        { name: 'Cancelled', value: s.cancelled, color: '#6B7280' },
      ].filter((d) => d.value > 0)
    : []

  return (
    <div className="flex flex-col gap-6 p-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Metrics</h1>
          <p className="text-sm text-text-muted mt-1">Visão geral de execuções e tokens</p>
        </div>
        <div className="flex items-center gap-3">
          <TimeRangeSelector value={range} onChange={setRange} />
          <Link to="/metrics/agents">
            <Button variant="secondary" size="sm">Por Agente</Button>
          </Link>
          <Link to="/metrics/providers">
            <Button variant="secondary" size="sm">Por Provider</Button>
          </Link>
        </div>
      </div>

      {/* Top metrics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Total Execuções" value={formatNumber(s?.total ?? 0)} />
        <MetricCard label="Completadas" value={formatNumber(s?.completed ?? 0)} sub="execuções" />
        <MetricCard label="Taxa de Falha" value={`${failRate}%`} alert={Number(failRate) > 10} />
        <MetricCard label="Avg Duration" value={s ? formatDuration(s.avgDurationMs) : '—'} />
      </div>

      {/* Timeseries + Gauge */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        <Card title="Execuções por período" className="lg:col-span-2">
          <TimeseriesChart
            data={buckets as Record<string, unknown>[]}
            xKey="bucket"
            series={[
              { key: 'completed', label: 'Completadas', color: '#10B981' },
              { key: 'failed', label: 'Falhas', color: '#EF4444' },
            ]}
            height={260}
          />
        </Card>
        <Card title="Slots Ativos">
          <div className="flex flex-col gap-4 py-4">
            <GaugeChart
              value={s?.running ?? 0}
              max={Math.max(s?.running ?? 0, 10)}
              label="Execuções Running"
              thresholds={{ green: 5, yellow: 8 }}
            />
            <GaugeChart
              value={s?.pending ?? 0}
              max={Math.max(s?.pending ?? 0, 20)}
              label="Execuções Pending"
              thresholds={{ green: 5, yellow: 15 }}
            />
          </div>
        </Card>
      </div>

      {/* Donut + Latency */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Distribuição de Status">
          <DonutChart data={donutData} height={240} />
        </Card>
        <Card title="Latência de Percentis">
          <div className="grid grid-cols-2 gap-4 mt-2">
            <MetricCard label="p50" value={s ? formatDuration(s.p50Ms) : '—'} sub="mediana" />
            <MetricCard label="p95" value={s ? formatDuration(s.p95Ms) : '—'} sub="95% das req." />
          </div>
          {t && (
            <div className="grid grid-cols-3 gap-4 mt-4">
              <MetricCard label="Input Tokens" value={formatNumber(t.totalInput)} />
              <MetricCard label="Output Tokens" value={formatNumber(t.totalOutput)} />
              <MetricCard
                label="Total Calls"
                value={formatNumber(t.totalCalls)}
                sub={`avg ${formatDuration(t.avgDurationMs)}`}
              />
            </div>
          )}
        </Card>
      </div>
    </div>
  )
}
