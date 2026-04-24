import { useState, useMemo } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useProjectsSummary } from '../../api/token-usage'
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

interface ProjectRow {
  projectId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
  estimatedCost: number
}

export function ProjectCostPage() {
  const [range, setRange] = useState<TimeRange>('7d')
  const from = useMemo(() => getFromDate(range), [range])

  const { data: rows = [], isLoading, error, refetch } = useProjectsSummary({ from })
  const { data: priceList = [] } = useModelPricings()

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar custos por projeto" onRetry={refetch} />

  // API devolve uma linha por (projectId, modelId) — agregamos por projectId
  const byProject = rows.reduce<Record<string, ProjectRow>>((acc, r) => {
    const cost = estimateCost(r.totalInput, r.totalOutput, r.modelId, priceList)
    if (!acc[r.projectId]) {
      acc[r.projectId] = {
        projectId: r.projectId,
        totalInput: 0,
        totalOutput: 0,
        totalTokens: 0,
        callCount: 0,
        avgDurationMs: r.avgDurationMs,
        estimatedCost: 0,
      }
    }
    acc[r.projectId]!.totalInput += r.totalInput
    acc[r.projectId]!.totalOutput += r.totalOutput
    acc[r.projectId]!.totalTokens += r.totalTokens
    acc[r.projectId]!.callCount += r.callCount
    acc[r.projectId]!.estimatedCost += cost
    return acc
  }, {})

  const projects = Object.values(byProject).sort((a, b) => b.totalTokens - a.totalTokens)

  const totalCost = projects.reduce((s, p) => s + p.estimatedCost, 0)
  const totalTokens = projects.reduce((s, p) => s + p.totalTokens, 0)
  const totalCalls = projects.reduce((s, p) => s + p.callCount, 0)

  const donutData = projects.map((p, i) => ({
    name: p.projectId,
    value: p.estimatedCost > 0 ? p.estimatedCost : p.totalTokens,
    color: CHART_COLORS[i % CHART_COLORS.length] ?? '#6B7280',
  }))

  const columns: ColumnDef<ProjectRow, unknown>[] = [
    {
      accessorKey: 'projectId',
      header: 'Projeto',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-primary font-medium">{String(getValue())}</span>
      ),
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
          <h1 className="text-2xl font-bold text-text-primary">Custo por Projeto</h1>
          <p className="text-sm text-text-muted mt-1">Consumo de tokens e custo estimado por projeto</p>
        </div>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <MetricCard label="Custo Total Estimado" value={`$${totalCost.toFixed(4)}`} sub="baseado em pricing" />
        <MetricCard label="Total Tokens" value={formatNumber(totalTokens)} />
        <MetricCard label="Total Calls" value={formatNumber(totalCalls)} />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card title="Distribuição por Projeto">
          {donutData.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum projeto com tokens neste período." />
          ) : (
            <DonutChart data={donutData} height={260} />
          )}
        </Card>
        <Card title="Detalhamento por Projeto" padding={false}>
          {projects.length === 0 ? (
            <EmptyState title="Sem dados" description="Nenhum registro neste período." />
          ) : (
            <DataTable
              data={projects}
              columns={columns}
              searchPlaceholder="Buscar projeto..."
            />
          )}
        </Card>
      </div>
    </div>
  )
}
