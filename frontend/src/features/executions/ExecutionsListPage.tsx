import { useState, useEffect, useRef } from 'react'
import { useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { StatusPill } from '../../shared/data/StatusPill'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Card } from '../../shared/ui/Card'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { formatDuration } from '../../shared/utils/formatters'
import {
  useExecutions,
  useCancelExecution,
  type WorkflowExecution,
  type ExecutionListParams,
} from '../../api/executions'

const STATUS_OPTIONS = [
  { label: 'Todos', value: '' },
  { label: 'Pending', value: 'Pending' },
  { label: 'Running', value: 'Running' },
  { label: 'Completed', value: 'Completed' },
  { label: 'Failed', value: 'Failed' },
  { label: 'Cancelled', value: 'Cancelled' },
  { label: 'Paused', value: 'Paused' },
]

type ExecutionStatus = WorkflowExecution['status']

export function ExecutionsListPage() {
  const navigate = useNavigate()

  const [status, setStatus] = useState('')
  const [workflowId, setWorkflowId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [cancelTarget, setCancelTarget] = useState<WorkflowExecution | null>(null)

  const params: ExecutionListParams = {
    status: status || undefined,
    workflowId: workflowId || undefined,
    from: from || undefined,
    to: to || undefined,
  }

  const { data, isLoading, error, refetch } = useExecutions(params)
  const cancelExecution = useCancelExecution()

  const executions = data?.items ?? []

  // Auto-refresh when there are Running or Pending executions
  const hasActive = executions.some(
    (e) => e.status === 'Running' || e.status === 'Pending'
  )
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)

  useEffect(() => {
    if (hasActive) {
      intervalRef.current = setInterval(() => refetch(), 10_000)
    }
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    }
  }, [hasActive, refetch])

  const handleCancel = () => {
    if (!cancelTarget) return
    cancelExecution.mutate(cancelTarget.executionId, {
      onSuccess: () => setCancelTarget(null),
    })
  }

  const columns: ColumnDef<WorkflowExecution, unknown>[] = [
    {
      accessorKey: 'executionId',
      header: 'Execution ID',
      cell: ({ getValue }) => {
        const id = getValue() as string
        return (
          <span className="font-mono text-xs text-text-secondary">{id.slice(0, 8)}…</span>
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
      cell: ({ getValue }) => (
        <StatusPill status={getValue() as ExecutionStatus} />
      ),
    },
    {
      accessorKey: 'startedAt',
      header: 'Iniciado em',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-muted">
          {new Date(getValue() as string).toLocaleString('pt-BR')}
        </span>
      ),
    },
    {
      id: 'duration',
      header: 'Duração',
      cell: ({ row }) => {
        const { startedAt, completedAt } = row.original
        if (!completedAt) return <span className="text-sm text-text-muted">—</span>
        const ms = new Date(completedAt).getTime() - new Date(startedAt).getTime()
        return <span className="text-sm text-text-secondary">{formatDuration(ms)}</span>
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => {
        const exec = row.original
        const isActive = exec.status === 'Running' || exec.status === 'Pending'
        return (
          <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
            {isActive && (
              <Button
                variant="danger"
                size="sm"
                onClick={() => setCancelTarget(exec)}
              >
                Cancelar
              </Button>
            )}
          </div>
        )
      },
    },
  ]

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar execuções." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Execuções</h1>
          <p className="text-sm text-text-muted mt-1">
            Monitore e gerencie as execuções de workflows.
            {hasActive && (
              <span className="ml-2 text-accent-blue">Auto-refresh ativo</span>
            )}
          </p>
        </div>
        <Button variant="secondary" size="sm" onClick={() => refetch()}>
          Atualizar
        </Button>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <Select
            label="Status"
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            options={STATUS_OPTIONS}
          />
          <Input
            label="Workflow ID"
            value={workflowId}
            onChange={(e) => setWorkflowId(e.target.value)}
            placeholder="Filtrar por workflow"
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
        data={executions}
        columns={columns}
        searchPlaceholder="Buscar execução..."
        onRowClick={(row) => navigate(`/executions/${row.executionId}`)}
      />

      <ConfirmDialog
        open={!!cancelTarget}
        onClose={() => setCancelTarget(null)}
        onConfirm={handleCancel}
        title="Cancelar Execução"
        message={`Tem certeza que deseja cancelar a execução "${cancelTarget?.executionId.slice(0, 8)}..."?`}
        confirmLabel="Cancelar Execução"
        variant="danger"
        loading={cancelExecution.isPending}
      />
    </div>
  )
}
