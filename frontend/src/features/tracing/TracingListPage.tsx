import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useExecutions } from '../../api/executions'
import type { WorkflowExecution } from '../../api/executions'
import { Card } from '../../shared/ui/Card'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { StatusPill } from '../../shared/data/StatusPill'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatDuration } from '../../shared/utils/formatters'

const STATUS_OPTIONS = [
  { value: '', label: 'Todos' },
  { value: 'Running', label: 'Running' },
  { value: 'Completed', label: 'Completed' },
  { value: 'Failed', label: 'Failed' },
  { value: 'Cancelled', label: 'Cancelled' },
  { value: 'Paused', label: 'Paused' },
]

function DurationBar({ execution }: { execution: WorkflowExecution }) {
  const started = new Date(execution.startedAt).getTime()
  const ended = execution.completedAt ? new Date(execution.completedAt).getTime() : Date.now()
  const ms = ended - started

  const color =
    execution.status === 'Completed' ? 'bg-emerald-500' :
    execution.status === 'Failed' ? 'bg-red-500' :
    execution.status === 'Running' ? 'bg-blue-400 animate-pulse' :
    'bg-amber-500'

  return (
    <span className="flex items-center gap-2">
      <span className={`inline-block h-2 w-16 rounded-full opacity-70 ${color}`} />
      <span className="text-xs text-text-muted tabular-nums">{formatDuration(ms)}</span>
    </span>
  )
}

export function TracingListPage() {
  const navigate = useNavigate()
  const [workflowId, setWorkflowId] = useState('')
  const [status, setStatus] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  const params = {
    ...(workflowId ? { workflowId } : {}),
    ...(status ? { status } : {}),
    ...(from ? { from: new Date(from).toISOString() } : {}),
    ...(to ? { to: new Date(to).toISOString() } : {}),
    pageSize: 50,
  }

  const { data, isLoading, error, refetch } = useExecutions(params)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar execuções" onRetry={refetch} />

  const items = data?.items ?? []

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Tracing</h1>
        <p className="text-sm text-text-muted mt-1">
          Execuções de workflow — clique para ver o trace detalhado
        </p>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <Input
            label="Workflow ID"
            value={workflowId}
            onChange={(e) => setWorkflowId(e.target.value)}
            placeholder="ex: resgate-investimento"
          />
          <Select
            label="Status"
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            options={STATUS_OPTIONS}
          />
          <Input
            label="De"
            type="datetime-local"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
          <Input
            label="Até"
            type="datetime-local"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
        </div>
      </Card>

      <Card title={`Execuções (${data?.total ?? 0})`} padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhuma execução encontrada"
            description="Execute um workflow para ver os traces aqui."
          />
        ) : (
          <div className="divide-y divide-border-primary/40">
            {/* Header */}
            <div className="grid grid-cols-[1fr_160px_110px_160px_180px] gap-4 px-4 py-2 text-[10px] font-semibold text-text-muted uppercase tracking-wider">
              <span>Execution ID</span>
              <span>Workflow</span>
              <span>Status</span>
              <span>Início</span>
              <span>Duração</span>
            </div>

            {items.map((ex) => (
              <div
                key={ex.executionId}
                onClick={() => navigate(`/tracing/${ex.executionId}`)}
                className="grid grid-cols-[1fr_160px_110px_160px_180px] gap-4 px-4 py-3 cursor-pointer hover:bg-bg-tertiary/60 transition-colors"
              >
                <span className="font-mono text-xs text-accent-blue truncate">
                  {ex.executionId}
                </span>
                <span className="text-xs text-text-secondary truncate">{ex.workflowId}</span>
                <StatusPill status={ex.status} />
                <span className="text-xs text-text-muted">
                  {new Date(ex.startedAt).toLocaleString('pt-BR')}
                </span>
                <DurationBar execution={ex} />
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}
