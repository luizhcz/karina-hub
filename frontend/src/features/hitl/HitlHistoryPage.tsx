import { useState } from 'react'
import { useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Badge } from '../../shared/ui/Badge'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Card } from '../../shared/ui/Card'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import {
  usePendingInteractions,
  useInteractionsByExecution,
  type HumanInteraction,
} from '../../api/interactions'

// API só expõe pending e por-execução. Não há endpoint dedicado de histórico,
// então a listagem aqui reusa o pending (filtrando no cliente).

const STATUS_OPTIONS = [
  { label: 'Todos', value: '' },
  { label: 'Pending', value: 'Pending' },
  { label: 'Resolved', value: 'Resolved' },
  { label: 'Rejected', value: 'Rejected' },
]

function interactionStatusVariant(status: string): 'yellow' | 'green' | 'red' | 'gray' {
  switch (status) {
    case 'Pending': return 'yellow'
    case 'Resolved': return 'green'
    case 'Rejected': return 'red'
    default: return 'gray'
  }
}

export function HitlHistoryPage() {
  const navigate = useNavigate()

  const [statusFilter, setStatusFilter] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [executionIdFilter, setExecutionIdFilter] = useState('')

  // Use pending interactions as our data source (all interactions visible)
  const { data: allInteractions, isLoading, error, refetch } = usePendingInteractions()

  // Also pull per-execution if user filters by executionId
  const { data: execInteractions } = useInteractionsByExecution(
    executionIdFilter,
    !!executionIdFilter
  )

  const source: HumanInteraction[] = executionIdFilter
    ? (execInteractions ?? [])
    : (allInteractions ?? [])

  const filtered = source.filter((i) => {
    if (statusFilter && i.status !== statusFilter) return false
    if (from && new Date(i.createdAt) < new Date(from)) return false
    if (to && new Date(i.createdAt) > new Date(to + 'T23:59:59')) return false
    return true
  })

  const columns: ColumnDef<HumanInteraction, unknown>[] = [
    {
      accessorKey: 'interactionId',
      header: 'Interaction ID',
      cell: ({ getValue }) => {
        const id = getValue() as string
        return (
          <span className="font-mono text-xs text-text-secondary">{id.slice(0, 12)}…</span>
        )
      },
    },
    {
      accessorKey: 'executionId',
      header: 'Execution ID',
      cell: ({ getValue }) => {
        const id = getValue() as string
        return (
          <span className="font-mono text-xs text-text-muted">{id.slice(0, 12)}…</span>
        )
      },
    },
    {
      accessorKey: 'workflowId',
      header: 'Workflow',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-secondary">{getValue() as string}</span>
      ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ getValue }) => {
        const status = getValue() as string
        return <Badge variant={interactionStatusVariant(status)}>{status}</Badge>
      },
    },
    {
      accessorKey: 'createdAt',
      header: 'Criado em',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-muted">
          {new Date(getValue() as string).toLocaleString('pt-BR')}
        </span>
      ),
    },
    {
      accessorKey: 'resolvedAt',
      header: 'Resolvido em',
      cell: ({ getValue }) => {
        const val = getValue() as string | undefined
        return (
          <span className="text-sm text-text-muted">
            {val ? new Date(val).toLocaleString('pt-BR') : '—'}
          </span>
        )
      },
    },
    {
      accessorKey: 'resolution',
      header: 'Resolução',
      cell: ({ getValue }) => {
        const val = getValue() as string | undefined
        if (!val) return <span className="text-sm text-text-muted">—</span>
        return (
          <span className="text-sm text-text-secondary truncate block max-w-[200px]" title={val}>
            {val.length > 40 ? val.slice(0, 40) + '…' : val}
          </span>
        )
      },
    },
  ]

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar histórico de interações." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Histórico de Interações</h1>
        <p className="text-sm text-text-muted mt-1">
          Visualize todas as interações HITL registradas.
        </p>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <Select
            label="Status"
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            options={STATUS_OPTIONS}
          />
          <Input
            label="Execution ID"
            value={executionIdFilter}
            onChange={(e) => setExecutionIdFilter(e.target.value)}
            placeholder="Filtrar por execução"
          />
          <Input
            label="De"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
            placeholder="YYYY-MM-DD"
          />
          <Input
            label="Até"
            value={to}
            onChange={(e) => setTo(e.target.value)}
            placeholder="YYYY-MM-DD"
          />
        </div>
      </Card>

      <DataTable
        data={filtered}
        columns={columns}
        searchPlaceholder="Buscar interação..."
        onRowClick={(row) => navigate(`/hitl/${row.interactionId}`)}
      />
    </div>
  )
}
