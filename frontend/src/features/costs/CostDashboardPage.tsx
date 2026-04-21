import { useState, useMemo } from 'react'
import { Link } from 'react-router'
import { useTokenSummary, useThroughput } from '../../api/token-usage'
import { useModelPricings } from '../../api/pricing'
import { Card } from '../../shared/ui/Card'
import { MetricCard } from '../../shared/data/MetricCard'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { TimeseriesChart } from '../../shared/charts/TimeseriesChart'
import { DonutChart } from '../../shared/charts/DonutChart'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatNumber } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'
import { CHART_COLORS } from '../../constants/theme'

function estimateCostFromTokens(
  input: number,
  output: number,
  modelId: string,
  pricings: Array<{ modelId: string; pricePerInputToken: number; pricePerOutputToken: number }>
): number {
  const p = pricings.find((x) => x.modelId === modelId)
  if (!p) return 0
  return input * p.pricePerInputToken + output * p.pricePerOutputToken
}

export function CostDashboardPage() {
  const [range, setRange] = useState<TimeRange>('7d')
  const from = useMemo(() => getFromDate(range), [range])

  const tokenSummary = useTokenSummary({ from })
  const throughput = useThroughput({ from })
  const pricings = useModelPricings()

  if (tokenSummary.isLoading) return <PageLoader />
  if (tokenSummary.error) return <ErrorCard message="Erro ao carregar dados de custo" onRetry={tokenSummary.refetch} />

  const summary = tokenSummary.data
  const buckets = throughput.data?.buckets ?? []
  const priceList = pricings.data ?? []

  const byModel = (summary?.byAgent ?? []).reduce<Record<string, { input: number; output: number }>>(
    (acc, a) => {
      if (!acc[a.modelId]) acc[a.modelId] = { input: 0, output: 0 }
      acc[a.modelId]!.input += a.totalInput
      acc[a.modelId]!.output += a.totalOutput
      return acc
    },
    {}
  )

  const donutData = Object.entries(byModel).map(([modelId, { input, output }], i) => ({
    name: modelId,
    value: estimateCostFromTokens(input, output, modelId, priceList) || (input + output) * 0.000001,
    color: CHART_COLORS[i % CHART_COLORS.length] ?? '#6B7280',
  }))

  const chartData = buckets.map((b) => ({
    bucket: b.bucket.slice(11, 16),
    tokens: b.tokens,
    calls: b.llmCalls,
  }))

  const totalCostEst = donutData.reduce((s, d) => s + d.value, 0)
  const avgPerCall = summary && summary.totalCalls > 0 ? totalCostEst / summary.totalCalls : 0

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Cost Dashboard</h1>
          <p className="text-sm text-text-muted mt-1">Visão geral de custos de tokens e modelos</p>
        </div>
        <div className="flex items-center gap-3">
          <TimeRangeSelector value={range} onChange={setRange} />
          <Link to="/costs/pricing">
            <Button variant="secondary" size="sm">Gerenciar Pricing</Button>
          </Link>
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Custo Estimado" value={`$${totalCostEst.toFixed(4)}`} sub="baseado em pricing" />
        <MetricCard label="Total Tokens" value={formatNumber(summary?.totalTokens ?? 0)} />
        <MetricCard label="Total Calls" value={formatNumber(summary?.totalCalls ?? 0)} />
        <MetricCard label="Custo / Call" value={`$${avgPerCall.toFixed(6)}`} sub="estimativa" />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Tokens por Período">
          {chartData.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum dado de throughput neste período." />
          ) : (
            <TimeseriesChart
              data={chartData as Record<string, unknown>[]}
              xKey="bucket"
              series={[
                { key: 'tokens', label: 'Tokens', color: '#0057E0' },
                { key: 'calls', label: 'Calls', color: '#10B981' },
              ]}
              height={260}
            />
          )}
        </Card>
        <Card title="Custo Estimado por Modelo">
          {donutData.length === 0 ? (
            <EmptyState
              title="Sem dados"
              description="Nenhum dado de custo disponível. Configure os preços em Pricing."
            />
          ) : (
            <DonutChart data={donutData} height={260} />
          )}
        </Card>
      </div>

      <Card title="Throughput">
        <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
          <MetricCard
            label="Avg Execuções/h"
            value={throughput.data?.avgExecutionsPerHour.toFixed(1) ?? '—'}
          />
          <MetricCard
            label="Avg Tokens/h"
            value={formatNumber(throughput.data?.avgTokensPerHour ?? 0)}
          />
          <MetricCard
            label="Avg Calls/h"
            value={throughput.data?.avgCallsPerHour.toFixed(1) ?? '—'}
          />
        </div>
      </Card>
    </div>
  )
}
