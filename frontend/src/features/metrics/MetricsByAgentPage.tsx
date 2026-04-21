import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useTokenSummary } from '../../api/token-usage'
import { DataTable } from '../../shared/data/DataTable'
import { MetricCard } from '../../shared/data/MetricCard'
import { MiniBarChart } from '../../shared/data/MiniBarChart'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { Card } from '../../shared/ui/Card'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatDuration, formatNumber } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'

interface AgentRow {
  agentId: string
  modelId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
}

export function MetricsByAgentPage() {
  const [range, setRange] = useState<TimeRange>('24h')
  const from = getFromDate(range)

  const { data: summary, isLoading, error, refetch } = useTokenSummary({ from })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar métricas por agente" onRetry={refetch} />

  const agents: AgentRow[] = summary?.byAgent ?? []

  const columns: ColumnDef<AgentRow, unknown>[] = [
    {
      accessorKey: 'agentId',
      header: 'Agent ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue()).slice(0, 20)}…</span>
      ),
    },
    {
      accessorKey: 'modelId',
      header: 'Model',
      cell: ({ getValue }) => (
        <span className="text-xs text-accent-blue">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'callCount',
      header: 'LLM Calls',
      cell: ({ getValue }) => formatNumber(getValue() as number),
    },
    {
      accessorKey: 'avgDurationMs',
      header: 'Avg Duration',
      cell: ({ getValue }) => formatDuration(getValue() as number),
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
      id: 'sparkline',
      header: 'Trend',
      cell: ({ row }) => (
        <MiniBarChart
          data={[row.original.totalInput, row.original.totalOutput, row.original.callCount * 10]}
          color="bg-accent-blue"
        />
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Metrics por Agente</h1>
          <p className="text-sm text-text-muted mt-1">Consumo de tokens e latência por agente</p>
        </div>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      {summary && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <MetricCard label="Total Input" value={formatNumber(summary.totalInput)} sub="tokens" />
          <MetricCard label="Total Output" value={formatNumber(summary.totalOutput)} sub="tokens" />
          <MetricCard label="Total Calls" value={formatNumber(summary.totalCalls)} />
          <MetricCard label="Avg Duration" value={formatDuration(summary.avgDurationMs)} />
        </div>
      )}

      <Card title="Agentes" padding={false}>
        {agents.length === 0 ? (
          <EmptyState
            title="Nenhum agente com dados"
            description="Não há registros de token usage neste período."
          />
        ) : (
          <DataTable
            data={agents}
            columns={columns}
            searchPlaceholder="Buscar agente..."
          />
        )}
      </Card>
    </div>
  )
}
