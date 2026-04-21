import { useState, useEffect, useRef } from 'react'
import { useParams, useNavigate } from 'react-router'
import { StatusPill } from '../../shared/data/StatusPill'
import { MetricCard } from '../../shared/data/MetricCard'
import { JsonViewer } from '../../shared/data/JsonViewer'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Card } from '../../shared/ui/Card'
import { Tabs } from '../../shared/ui/Tabs'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { formatDuration } from '../../shared/utils/formatters'
import {
  useExecutionFull,
  useCancelExecution,
  type WorkflowExecution,
  type ExecutionEventRecord,
} from '../../api/executions'

const TABS = [
  { key: "overview", label: "Overview" },
  { key: "events", label: "Events" },
  { key: "io", label: "Input / Output" },
  { key: "error", label: "Error" },
]

// ── Helpers ───────────────────────────────────────────────────────────────────

function calcDuration(exec: WorkflowExecution): string {
  if (!exec.completedAt) {
    const ms = Date.now() - new Date(exec.startedAt).getTime()
    return formatDuration(ms) + ' (em andamento)'
  }
  const ms = new Date(exec.completedAt).getTime() - new Date(exec.startedAt).getTime()
  return formatDuration(ms)
}

function statusBadgeVariant(status: WorkflowExecution['status']): 'green' | 'red' | 'blue' | 'yellow' | 'gray' | 'purple' {
  switch (status) {
    case 'Completed': return 'green'
    case 'Failed': return 'red'
    case 'Running': return 'blue'
    case 'Pending': return 'yellow'
    case 'Paused': return 'purple'
    case 'Cancelled': return 'gray'
  }
}

// ── Timeline ──────────────────────────────────────────────────────────────────

interface TimelineProps {
  events: ExecutionEventRecord[]
}

function EventTimeline({ events }: TimelineProps) {
  if (events.length === 0) {
    return <p className="text-sm text-text-muted">Nenhum evento registrado.</p>
  }

  return (
    <div className="flex flex-col gap-2">
      {events.slice(0, 20).map((ev, i) => (
        <div key={i} className="flex gap-3">
          <div className="flex flex-col items-center">
            <div className="w-2 h-2 rounded-full bg-accent-blue mt-1.5 flex-shrink-0" />
            {i < events.length - 1 && (
              <div className="w-px flex-1 bg-border-primary mt-1" />
            )}
          </div>
          <div className="pb-3 min-w-0">
            <p className="text-xs font-medium text-text-primary">{ev.eventType}</p>
            <p className="text-[10px] text-text-muted">
              {new Date(ev.timestamp).toLocaleString('pt-BR')}
            </p>
            {ev.payload && (
              <p className="text-xs text-text-secondary mt-0.5 truncate max-w-md">{ev.payload}</p>
            )}
          </div>
        </div>
      ))}
      {events.length > 20 && (
        <p className="text-xs text-text-muted pl-5">+ {events.length - 20} eventos adicionais</p>
      )}
    </div>
  )
}

// ── Events Table ──────────────────────────────────────────────────────────────

interface EventsTableProps {
  events: ExecutionEventRecord[]
}

function EventsTable({ events }: EventsTableProps) {
  if (events.length === 0) {
    return <p className="text-sm text-text-muted">Nenhum evento encontrado.</p>
  }

  return (
    <div className="overflow-auto rounded-xl border border-border-primary">
      <table className="w-full text-sm">
        <thead>
          <tr className="bg-bg-tertiary border-b border-border-primary">
            <th className="px-4 py-2.5 text-left text-xs font-semibold text-text-muted uppercase tracking-wider">Tipo</th>
            <th className="px-4 py-2.5 text-left text-xs font-semibold text-text-muted uppercase tracking-wider">Payload</th>
            <th className="px-4 py-2.5 text-left text-xs font-semibold text-text-muted uppercase tracking-wider">Timestamp</th>
          </tr>
        </thead>
        <tbody>
          {events.map((ev, i) => (
            <tr key={i} className="border-b border-border-primary/50 hover:bg-bg-tertiary/50 transition-colors">
              <td className="px-4 py-2.5">
                <span className="text-xs font-mono text-accent-blue">{ev.eventType}</span>
              </td>
              <td className="px-4 py-2.5 max-w-xs">
                <span className="text-xs text-text-secondary truncate block">{ev.payload ?? '—'}</span>
              </td>
              <td className="px-4 py-2.5">
                <span className="text-xs text-text-muted">
                  {new Date(ev.timestamp).toLocaleString('pt-BR')}
                </span>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

// ── Main Component ────────────────────────────────────────────────────────────

export function ExecutionDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [activeTab, setActiveTab] = useState('overview')
  const [showCancelConfirm, setShowCancelConfirm] = useState(false)

  const { data, isLoading, error, refetch } = useExecutionFull(id ?? '', !!id)
  const cancelExecution = useCancelExecution()

  const exec = data?.execution
  const events = data?.events ?? []
  const isRunning = exec?.status === 'Running'

  // Auto-refresh every 5s when running
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  useEffect(() => {
    if (isRunning) {
      intervalRef.current = setInterval(() => refetch(), 5_000)
    }
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    }
  }, [isRunning, refetch])

  const handleCancel = () => {
    if (!id) return
    cancelExecution.mutate(id, {
      onSuccess: () => {
        setShowCancelConfirm(false)
        refetch()
      },
    })
  }

  if (!id) return <ErrorCard message="ID da execução não encontrado." />
  if (isLoading) return <PageLoader />
  if (error || !exec) return <ErrorCard message="Erro ao carregar execução." onRetry={refetch} />

  const isActive = exec.status === 'Running' || exec.status === 'Pending'

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div className="flex flex-col gap-2">
          <button
            onClick={() => navigate(-1)}
            className="text-xs text-text-muted hover:text-text-secondary flex items-center gap-1"
          >
            ← Voltar
          </button>
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-bold text-text-primary font-mono">
              {exec.executionId.slice(0, 16)}…
            </h1>
            <StatusPill status={exec.status} />
            <Badge variant={statusBadgeVariant(exec.status)}>{exec.status}</Badge>
            {isRunning && (
              <span className="text-xs text-accent-blue">Auto-refresh ativo</span>
            )}
          </div>
          <p className="text-sm text-text-muted">Workflow: {exec.workflowId}</p>
        </div>

        <div className="flex gap-2">
          {isActive && (
            <Button
              variant="danger"
              size="sm"
              onClick={() => setShowCancelConfirm(true)}
            >
              Cancelar
            </Button>
          )}
          <Button variant="secondary" size="sm" onClick={() => refetch()}>
            Atualizar
          </Button>
        </div>
      </div>

      {/* Metrics grid */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <MetricCard label="Duração" value={calcDuration(exec)} />
        <MetricCard
          label="Iniciado em"
          value={new Date(exec.startedAt).toLocaleTimeString('pt-BR')}
          sub={new Date(exec.startedAt).toLocaleDateString('pt-BR')}
        />
        <MetricCard
          label="Concluído em"
          value={exec.completedAt ? new Date(exec.completedAt).toLocaleTimeString('pt-BR') : '—'}
          sub={exec.completedAt ? new Date(exec.completedAt).toLocaleDateString('pt-BR') : undefined}
        />
        <MetricCard
          label="Status"
          value={exec.status}
        />
      </div>

      {/* Tabs */}
      <Tabs items={TABS} active={activeTab} onChange={setActiveTab} />

      {/* Tab Content */}
      {activeTab === 'overview' && (
        <Card title="Timeline de Eventos">
          <EventTimeline events={events} />
        </Card>
      )}

      {activeTab === 'events' && (
        <div className="flex flex-col gap-3">
          <div className="flex items-center justify-between">
            <h2 className="text-sm font-semibold text-text-primary">
              {events.length} evento(s)
            </h2>
            {isRunning && (
              <Button variant="ghost" size="sm" onClick={() => refetch()}>
                Atualizar
              </Button>
            )}
          </div>
          <EventsTable events={events} />
        </div>
      )}

      {activeTab === 'io' && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold text-text-primary">Input</h2>
            {exec.input ? (
              <JsonViewer data={exec.input} collapsed={false} />
            ) : (
              <p className="text-sm text-text-muted">Sem input registrado.</p>
            )}
          </div>
          <div className="flex flex-col gap-2">
            <h2 className="text-sm font-semibold text-text-primary">Output</h2>
            {exec.output ? (
              <JsonViewer data={exec.output} collapsed={false} />
            ) : (
              <p className="text-sm text-text-muted">
                {isRunning ? 'Execução em andamento...' : 'Sem output registrado.'}
              </p>
            )}
          </div>
        </div>
      )}

      {activeTab === 'error' && (
        exec.status === 'Failed' && exec.errorMessage ? (
          <Card className="border-red-500/30 bg-red-500/5">
            <div className="flex flex-col gap-2">
              <p className="text-sm font-semibold text-red-400">Erro na Execução</p>
              <pre className="text-xs text-red-300 whitespace-pre-wrap font-mono bg-red-500/10 rounded-lg p-3">
                {exec.errorMessage}
              </pre>
            </div>
          </Card>
        ) : (
          <div className="flex items-center justify-center py-12">
            <p className="text-sm text-text-muted">
              {exec.status === 'Failed' ? 'Sem detalhes de erro disponíveis.' : 'Execução não falhou.'}
            </p>
          </div>
        )
      )}

      {/* Cancel confirm */}
      <ConfirmDialog
        open={showCancelConfirm}
        onClose={() => setShowCancelConfirm(false)}
        onConfirm={handleCancel}
        title="Cancelar Execução"
        message="Tem certeza que deseja cancelar esta execução?"
        confirmLabel="Cancelar Execução"
        variant="danger"
        loading={cancelExecution.isPending}
      />
    </div>
  )
}
