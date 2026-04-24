import { useState, useMemo } from 'react'
import { Link } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useWorkflowsSummary } from '../../api/token-usage'
import { useModelPricings } from '../../api/pricing'
import { Card } from '../../shared/ui/Card'
import { MetricCard } from '../../shared/data/MetricCard'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { DonutChart } from '../../shared/charts/DonutChart'
import { DataTable } from '../../shared/data/DataTable'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatNumber, formatDuration } from '../../shared/utils/formatters'
import { getFromDate, type TimeRange } from '../../shared/utils/date'
import { CHART_COLORS } from '../../constants/theme'

function estimateCost(
  input: number,
  output: number,
  modelId: string,
  pricings: Array<{ modelId: string; pricePerInputToken: number; pricePerOutputToken: number }>
): number {
  const p = pricings.find((x) => x.modelId === modelId)
  if (!p) return (input + output) * 0.000001 // fallback genérico quando sem pricing
  return input * p.pricePerInputToken + output * p.pricePerOutputToken
}

interface WorkflowRow {
  workflowId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
  estimatedCost: number
}

export function WorkflowCostPage() {
  const [range, setRange] = useState<TimeRange>('7d')
  const from = useMemo(() => getFromDate(range), [range])

  const { data: rows = [], isLoading, error, refetch } = useWorkflowsSummary({ from })
  const { data: priceList = [] } = useModelPricings()

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar custos por workflow" onRetry={refetch} />

  // API devolve uma linha por (workflowId, modelId) — agregamos por workflowId
  const byWorkflow = rows.reduce<Record<string, WorkflowRow>>((acc, r) => {
    const cost = estimateCost(r.totalInput, r.totalOutput, r.modelId, priceList)
    if (!acc[r.workflowId]) {
      acc[r.workflowId] = {
        workflowId: r.workflowId,
        totalInput: 0,
        totalOutput: 0,
        totalTokens: 0,
        callCount: 0,
        avgDurationMs: r.avgDurationMs,
        estimatedCost: 0,
      }
    }
    acc[r.workflowId]!.totalInput += r.totalInput
    acc[r.workflowId]!.totalOutput += r.totalOutput
    acc[r.workflowId]!.totalTokens += r.totalTokens
    acc[r.workflowId]!.callCount += r.callCount
    acc[r.workflowId]!.estimatedCost += cost
    return acc
  }, {})

  const workflows = Object.values(byWorkflow).sort((a, b) => b.totalTokens - a.totalTokens)

  const totalCost = workflows.reduce((s, w) => s + w.estimatedCost, 0)
  const totalTokens = workflows.reduce((s, w) => s + w.totalTokens, 0)
  const totalCalls = workflows.reduce((s, w) => s + w.callCount, 0)

  const donutData = workflows.slice(0, 10).map((w, i) => ({
    name: w.workflowId.length > 20 ? w.workflowId.slice(0, 20) + '…' : w.workflowId,
    value: w.estimatedCost > 0 ? w.estimatedCost : w.totalTokens,
    color: CHART_COLORS[i % CHART_COLORS.length] ?? '#6B7280',
  }))

  const columns: ColumnDef<WorkflowRow, unknown>[] = [
    {
      accessorKey: 'workflowId',
      header: 'Workflow',
      cell: ({ getValue }) => {
        const id = String(getValue())
        return (
          <Link
            to={`/workflows/${id}`}
            className="font-mono text-xs text-accent-blue hover:underline"
          >
            {id.length > 28 ? id.slice(0, 28) + '…' : id}
          </Link>
        )
      },
    },
    {
      accessorKey: 'totalTokens',
      header: 'Total Tokens',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{formatNumber(getValue() as number)}</span>
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
      accessorKey: 'callCount',
      header: 'Calls',
      cell: ({ getValue }) => formatNumber(getValue() as number),
    },
    {
      accessorKey: 'avgDurationMs',
      header: 'Avg Duration',
      cell: ({ getValue }) => formatDuration(getValue() as number),
    },
    {
      accessorKey: 'estimatedCost',
      header: 'Custo Estimado',
      cell: ({ getValue }) => {
        const cost = getValue() as number
        return (
          <span className="text-amber-400 font-medium">
            {`$${cost.toFixed(6)}`}
          </span>
        )
      },
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Custo por Workflow</h1>
          <p className="text-sm text-text-muted mt-1">Consumo de tokens e custo estimado por workflow</p>
        </div>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <MetricCard label="Custo Total Estimado" value={`$${totalCost.toFixed(4)}`} sub="baseado em pricing" />
        <MetricCard label="Total Tokens" value={formatNumber(totalTokens)} />
        <MetricCard label="Total Calls" value={formatNumber(totalCalls)} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Top Workflows por Custo / Tokens">
          {donutData.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum workflow com tokens neste período." />
          ) : (
            <DonutChart data={donutData} height={260} />
          )}
        </Card>
        <Card title="Detalhamento por Workflow" padding={false}>
          {workflows.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum registro neste período." />
          ) : (
            <DataTable
              data={workflows}
              columns={columns}
              searchPlaceholder="Buscar workflow..."
            />
          )}
        </Card>
      </div>
    </div>
  )
}
