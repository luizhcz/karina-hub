import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useTokenSummary } from '../../api/token-usage'
import type { AgentTokenSummary } from '../../api/token-usage'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { MetricCard } from '../../shared/data/MetricCard'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { DonutChart } from '../../shared/charts/DonutChart'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatNumber, formatDuration } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'
import { CHART_COLORS } from '../../constants/theme'

export function TokenUsageAuditPage() {
  const [range, setRange] = useState<TimeRange>('7d')
  const from = getFromDate(range)

  const { data: summary, isLoading, error, refetch } = useTokenSummary({ from })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar dados de token usage" onRetry={refetch} />

  const agents = summary?.byAgent ?? []

  const byModel = agents.reduce<Record<string, number>>((acc, a) => {
    acc[a.modelId] = (acc[a.modelId] ?? 0) + a.totalTokens
    return acc
  }, {})

  const donutData = Object.entries(byModel).map(([name, value], i) => ({
    name,
    value,
    color: CHART_COLORS[i % CHART_COLORS.length] ?? '#6B7280',
  }))

  const columns: ColumnDef<AgentTokenSummary, unknown>[] = [
    {
      accessorKey: 'agentId',
      header: 'Agent ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue()).slice(0, 20)}…</span>
      ),
    },
    {
      accessorKey: 'modelId',
      header: 'Modelo',
      cell: ({ getValue }) => (
        <span className="text-xs text-accent-blue">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'totalInput',
      header: 'Input Tokens',
      cell: ({ getValue }) => formatNumber(getValue() as number),
    },
    {
      accessorKey: 'totalOutput',
      header: 'Output Tokens',
      cell: ({ getValue }) => formatNumber(getValue() as number),
    },
    {
      accessorKey: 'totalTokens',
      header: 'Total Tokens',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{formatNumber(getValue() as number)}</span>
      ),
    },
    {
      accessorKey: 'callCount',
      header: 'Calls',
      cell: ({ getValue }) => formatNumber(getValue() as number),
    },
    {
      accessorKey: 'avgDurationMs',
      header: 'Avg Duration',
      cell: ({ getValue }) => formatDuration(getValue() as number),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Token Usage Audit</h1>
          <p className="text-sm text-text-muted mt-1">Auditoria detalhada de consumo de tokens</p>
        </div>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Total Input" value={formatNumber(summary?.totalInput ?? 0)} sub="tokens" />
        <MetricCard label="Total Output" value={formatNumber(summary?.totalOutput ?? 0)} sub="tokens" />
        <MetricCard label="Total Tokens" value={formatNumber(summary?.totalTokens ?? 0)} />
        <MetricCard label="Total Calls" value={formatNumber(summary?.totalCalls ?? 0)} sub={`avg ${formatDuration(summary?.avgDurationMs ?? 0)}`} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Tokens por Modelo">
          {donutData.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum token usage neste período." />
          ) : (
            <DonutChart data={donutData} height={260} />
          )}
        </Card>
        <Card title="Registros por Agente" padding={false}>
          {agents.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum registro de token usage." />
          ) : (
            <DataTable
              data={agents}
              columns={columns}
              searchPlaceholder="Buscar agente..."
            />
          )}
        </Card>
      </div>
    </div>
  )
}
