import { useState, useEffect, useCallback } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { StatusPill } from '../../shared/data/StatusPill'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatDuration } from '../../shared/utils/formatters'

type JobStatus = 'Pending' | 'Running' | 'Completed' | 'Failed'
type JobType = 'CleanupOldExecutions' | 'RecalculatePricing' | 'ExportReport' | 'ReindexSearch'

interface BackgroundJob {
  jobId: string
  type: JobType
  status: JobStatus
  createdAt: string
  completedAt?: string
  errorMessage?: string
  metadata?: Record<string, string>
}

const TYPE_COLORS: Record<JobType, 'blue' | 'purple' | 'green' | 'yellow'> = {
  CleanupOldExecutions: 'blue',
  RecalculatePricing: 'purple',
  ExportReport: 'green',
  ReindexSearch: 'yellow',
}

const INITIAL_JOBS: BackgroundJob[] = [
  {
    jobId: crypto.randomUUID(),
    type: 'CleanupOldExecutions',
    status: 'Completed',
    createdAt: new Date(Date.now() - 3600000).toISOString(),
    completedAt: new Date(Date.now() - 3550000).toISOString(),
  },
  {
    jobId: crypto.randomUUID(),
    type: 'RecalculatePricing',
    status: 'Failed',
    createdAt: new Date(Date.now() - 7200000).toISOString(),
    completedAt: new Date(Date.now() - 7100000).toISOString(),
    errorMessage: 'Pricing API unavailable',
  },
]

let jobsStore: BackgroundJob[] = INITIAL_JOBS

export function BackgroundJobsPage() {
  const navigate = useNavigate()
  const [jobs, setJobs] = useState<BackgroundJob[]>(jobsStore)
  const [cancelId, setCancelId] = useState<string | null>(null)
  const [cancelling, setCancelling] = useState(false)

  const refresh = useCallback(() => {
    setJobs([...jobsStore])
  }, [])

  useEffect(() => {
    const hasActive = jobs.some((j) => j.status === 'Running' || j.status === 'Pending')
    if (!hasActive) return
    const id = setInterval(refresh, 15000)
    return () => clearInterval(id)
  }, [jobs, refresh])

  const handleCancel = () => {
    if (!cancelId) return
    setCancelling(true)
    setTimeout(() => {
      jobsStore = jobsStore.map((j) =>
        j.jobId === cancelId
          ? { ...j, status: 'Failed' as JobStatus, completedAt: new Date().toISOString(), errorMessage: 'Cancelado pelo usuário' }
          : j
      )
      setJobs([...jobsStore])
      setCancelId(null)
      setCancelling(false)
    }, 500)
  }

  const handleRetry = (id: string) => {
    jobsStore = jobsStore.map((j) =>
      j.jobId === id ? { ...j, status: 'Pending' as JobStatus, completedAt: undefined, errorMessage: undefined } : j
    )
    setJobs([...jobsStore])
  }

  const columns: ColumnDef<BackgroundJob, unknown>[] = [
    {
      accessorKey: 'jobId',
      header: 'Job ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue()).slice(0, 14)}…</span>
      ),
    },
    {
      accessorKey: 'type',
      header: 'Tipo',
      cell: ({ getValue }) => {
        const v = getValue() as JobType
        return <Badge variant={TYPE_COLORS[v] ?? 'gray'}>{v}</Badge>
      },
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ getValue }) => {
        const v = getValue() as string
        return <StatusPill status={v as 'Pending' | 'Running' | 'Completed' | 'Failed'} />
      },
    },
    {
      accessorKey: 'createdAt',
      header: 'Criado em',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted">{new Date(String(getValue())).toLocaleString('pt-BR')}</span>
      ),
    },
    {
      accessorKey: 'completedAt',
      header: 'Concluído em',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return <span className="text-xs text-text-muted">{v ? new Date(v).toLocaleString('pt-BR') : '—'}</span>
      },
    },
    {
      id: 'duration',
      header: 'Duração',
      cell: ({ row }) => {
        const start = new Date(row.original.createdAt).getTime()
        const end = row.original.completedAt ? new Date(row.original.completedAt).getTime() : null
        return <span className="text-xs text-text-muted">{end ? formatDuration(end - start) : '—'}</span>
      },
    },
    {
      id: 'actions',
      header: 'Ações',
      cell: ({ row }) => {
        const j = row.original
        return (
          <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
            {(j.status === 'Pending' || j.status === 'Running') && (
              <Button variant="danger" size="sm" onClick={() => setCancelId(j.jobId)}>
                Cancelar
              </Button>
            )}
            {j.status === 'Failed' && (
              <Button variant="secondary" size="sm" onClick={() => handleRetry(j.jobId)}>
                Retry
              </Button>
            )}
          </div>
        )
      },
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Background Jobs</h1>
          <p className="text-sm text-text-muted mt-1">
            {jobs.filter((j) => j.status === 'Running' || j.status === 'Pending').length} job(s) ativo(s)
            · auto-refresh a cada 15s quando há jobs ativos
          </p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" size="sm" onClick={refresh}>Atualizar</Button>
          <Button onClick={() => navigate('/background/new')}>Novo Job</Button>
        </div>
      </div>

      <Card padding={false}>
        {jobs.length === 0 ? (
          <EmptyState
            title="Nenhum job registrado"
            description="Crie um job em background para executar tarefas assíncronas."
            action={<Button onClick={() => navigate('/background/new')}>Novo Job</Button>}
          />
        ) : (
          <DataTable
            data={jobs}
            columns={columns}
            searchPlaceholder="Buscar job..."
          />
        )}
      </Card>

      {jobs.some((j) => j.status === 'Failed' && j.errorMessage) && (
        <Card title="Erros Recentes">
          <div className="flex flex-col gap-2">
            {jobs
              .filter((j) => j.status === 'Failed' && j.errorMessage)
              .slice(0, 5)
              .map((j) => (
                <div
                  key={j.jobId}
                  className="flex items-start gap-3 p-3 bg-red-500/5 border border-red-500/20 rounded-lg"
                >
                  <Badge variant="red">{j.type}</Badge>
                  <p className="text-xs text-red-300 flex-1">{j.errorMessage}</p>
                </div>
              ))}
          </div>
        </Card>
      )}

      <ConfirmDialog
        open={cancelId !== null}
        onClose={() => setCancelId(null)}
        onConfirm={handleCancel}
        title="Cancelar Job"
        message="Tem certeza que deseja cancelar este job? Tarefas em andamento serão interrompidas."
        confirmLabel="Cancelar Job"
        variant="danger"
        loading={cancelling}
      />
    </div>
  )
}
