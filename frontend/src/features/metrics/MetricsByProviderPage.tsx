import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useTokenSummary } from '../../api/token-usage'
import { DataTable } from '../../shared/data/DataTable'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { Card } from '../../shared/ui/Card'
import { DonutChart } from '../../shared/charts/DonutChart'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatDuration, formatNumber } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'

const PROVIDER_COLORS: Record<string, string> = {
  'gpt-4o': '#0057E0',
  'gpt-4': '#7C3AED',
  'gpt-3.5': '#10B981',
  'claude': '#F59E0B',
}

interface ProviderRow {
  modelId: string
  totalInput: number
  totalOutput: number
  callCount: number
  avgDurationMs: number
}

export function MetricsByProviderPage() {
  const [range, setRange] = useState<TimeRange>('24h')
  const from = getFromDate(range)

  const { data: summary, isLoading, error, refetch } = useTokenSummary({ from })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar métricas por provider" onRetry={refetch} />

  const byModel: ProviderRow[] = (summary?.byAgent ?? []).reduce((acc: ProviderRow[], cur) => {
    const existing = acc.find((r) => r.modelId === cur.modelId)
    if (existing) {
      existing.totalInput += cur.totalInput
      existing.totalOutput += cur.totalOutput
      existing.callCount += cur.callCount
      existing.avgDurationMs = (existing.avgDurationMs + cur.avgDurationMs) / 2
    } else {
      acc.push({
        modelId: cur.modelId,
        totalInput: cur.totalInput,
        totalOutput: cur.totalOutput,
        callCount: cur.callCount,
        avgDurationMs: cur.avgDurationMs,
      })
    }
    return acc
  }, [])

  const donutData = byModel.map((r, i) => ({
    name: r.modelId,
    value: r.totalInput + r.totalOutput,
    color: PROVIDER_COLORS[r.modelId] ?? `hsl(${i * 60}, 60%, 50%)`,
  }))

  const columns: ColumnDef<ProviderRow, unknown>[] = [
    {
      accessorKey: 'modelId',
      header: 'Modelo / Provider',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-accent-blue">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'callCount',
      header: 'LLM Calls',
      cell: ({ getValue }) => formatNumber(getValue() as number),
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
      accessorKey: 'avgDurationMs',
      header: 'Avg Latency',
      cell: ({ getValue }) => formatDuration(getValue() as number),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Metrics por Provider</h1>
          <p className="text-sm text-text-muted mt-1">Distribuição de uso por modelo/provider</p>
        </div>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Tokens por Modelo">
          {donutData.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum uso de token neste período." />
          ) : (
            <DonutChart data={donutData} height={280} />
          )}
        </Card>
        <Card title="Tabela de Providers" padding={false}>
          {byModel.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum uso de token neste período." />
          ) : (
            <DataTable
              data={byModel}
              columns={columns}
              searchPlaceholder="Buscar modelo..."
            />
          )}
        </Card>
      </div>
    </div>
  )
}
