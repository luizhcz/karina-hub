import { useState } from 'react'
import { useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { Tooltip } from '../../shared/ui/Tooltip'
import {
  useWorkflows,
  useDeleteWorkflow,
  useCloneWorkflow,
  useToggleWorkflowTrigger,
} from '../../api/workflows'
import type { WorkflowDef } from '../../api/workflows'
import { ApiError } from '../../api/client'

// ── Helpers ───────────────────────────────────────────────────────────────────

function modeVariant(mode: string): 'blue' | 'purple' | 'yellow' {
  if (mode === 'AgentOrchestration') return 'blue'
  if (mode === 'Executor') return 'purple'
  return 'yellow'
}

function modeLabel(mode: string): string {
  if (mode === 'AgentOrchestration') return 'Agent Orch.'
  if (mode === 'Executor') return 'Executor'
  if (mode === 'Mixed') return 'Mixed'
  return mode
}

function triggerVariant(type?: string): 'green' | 'blue' | 'purple' | 'gray' {
  if (type === 'Scheduled') return 'green'
  if (type === 'EventDriven') return 'purple'
  if (type === 'OnDemand') return 'blue'
  return 'gray'
}

// ── Component ─────────────────────────────────────────────────────────────────

export function WorkflowsListPage() {
  const navigate = useNavigate()
  const { data: workflows, isLoading, error, refetch } = useWorkflows()
  const deleteMutation = useDeleteWorkflow()
  const cloneMutation = useCloneWorkflow()
  const toggleTriggerMutation = useToggleWorkflowTrigger()

  const [deleteTarget, setDeleteTarget] = useState<WorkflowDef | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)

  const handleToggleTrigger = (workflow: WorkflowDef, enabled: boolean) => {
    setTogglingId(workflow.id)
    toggleTriggerMutation.mutate(
      { workflow, enabled },
      { onSettled: () => setTogglingId(null) },
    )
  }

  const handleClone = (workflow: WorkflowDef) => {
    cloneMutation.mutate({ id: workflow.id })
  }

  const columns: ColumnDef<WorkflowDef, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <div>
          <p className="font-medium text-text-primary">{row.original.name}</p>
          {row.original.description && (
            <p className="text-xs text-text-muted mt-0.5 truncate max-w-[220px]">
              {row.original.description}
            </p>
          )}
        </div>
      ),
    },
    {
      accessorKey: 'orchestrationMode',
      header: 'Mode',
      cell: ({ row, getValue }) => {
        const mode = getValue() as string
        const inputMode = row.original.configuration?.inputMode
        return (
          <div className="flex items-center gap-1.5 flex-wrap">
            <Badge variant={modeVariant(mode)}>{modeLabel(mode)}</Badge>
            {inputMode?.toLowerCase() === 'chat' && (
              <Badge variant="purple">Chat</Badge>
            )}
          </div>
        )
      },
    },
    {
      id: 'triggerType',
      header: 'Trigger',
      cell: ({ row }) => {
        const type = row.original.trigger?.type
        return (
          <Badge variant={triggerVariant(type)}>
            {type ?? 'OnDemand'}
          </Badge>
        )
      },
    },
    {
      id: 'agentsCount',
      header: 'Agents',
      cell: ({ row }) => (
        <span className="text-sm text-text-secondary">
          {row.original.agents?.length ?? 0}
        </span>
      ),
    },
    {
      id: 'lastExecution',
      header: 'Last Execution',
      cell: ({ row }) => {
        const fired = row.original.trigger?.lastFiredAt
        return (
          <span className="text-sm text-text-muted">
            {fired ? new Date(fired).toLocaleString('pt-BR') : '—'}
          </span>
        )
      },
    },
    {
      id: 'triggerStatus',
      header: 'Trigger Active',
      cell: ({ row }) => {
        const wf = row.original
        const hasScheduled = wf.trigger && wf.trigger.type !== 'OnDemand'
        const isEnabled = wf.trigger?.enabled ?? false
        const isToggling = togglingId === wf.id

        if (!hasScheduled) {
          return <span className="text-xs text-text-dimmed">—</span>
        }

        return (
          <Tooltip content={isEnabled ? 'Disable trigger' : 'Enable trigger'}>
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                handleToggleTrigger(wf, !isEnabled)
              }}
              disabled={isToggling}
              className={`
                relative inline-flex h-5 w-9 items-center rounded-full transition-colors
                ${isEnabled ? 'bg-accent-blue' : 'bg-bg-tertiary border border-border-secondary'}
                ${isToggling ? 'opacity-50 cursor-not-allowed' : 'cursor-pointer'}
              `}
            >
              <span
                className={`
                  inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition-transform
                  ${isEnabled ? 'translate-x-4' : 'translate-x-0.5'}
                `}
              />
            </button>
          </Tooltip>
        )
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => {
        const wf = row.original
        return (
          <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => navigate(`/workflows/${wf.id}`)}
            >
              Edit
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => navigate(`/workflows/${wf.id}/diagram`)}
            >
              Diagram
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => navigate(`/workflows/${wf.id}/trigger`)}
            >
              Trigger
            </Button>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => handleClone(wf)}
              loading={cloneMutation.isPending && cloneMutation.variables?.id === wf.id}
            >
              Clone
            </Button>
            <Button
              variant="danger"
              size="sm"
              onClick={() => setDeleteTarget(wf)}
            >
              Delete
            </Button>
          </div>
        )
      },
    },
  ]

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar workflows." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Workflows</h1>
          <p className="text-sm text-text-muted mt-1">
            Gerencie os workflows de orquestração de agentes.
          </p>
        </div>
        <Button onClick={() => navigate('/workflows/new')}>
          + Criar Workflow
        </Button>
      </div>

      {/* Table */}
      <DataTable
        data={workflows ?? []}
        columns={columns}
        searchPlaceholder="Buscar workflow por nome..."
        onRowClick={(row) => navigate(`/workflows/${row.id}`)}
      />

      {/* Delete confirmation */}
      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget) {
            deleteMutation.mutate(deleteTarget.id, {
              onSuccess: () => setDeleteTarget(null),
            })
          }
        }}
        title="Excluir Workflow"
        message={`Tem certeza que deseja excluir o workflow "${deleteTarget?.name}"? Esta acao nao pode ser desfeita.`}
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteMutation.isPending}
      />
    </div>
  )
}
