import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgents, useDeleteAgent } from '../../api/agents'
import type { AgentDef } from '../../api/agents'
import { ApiError } from '../../api/client'
import { useFunctions } from '../../api/tools'
import { ActivePromptBadge } from './components/ActivePromptBadge'

const PHASE_STYLE: Record<string, { label: string; bg: string }> = {
  Pre:  { label: 'Pre',  bg: 'bg-yellow-500/15 text-yellow-400' },
  Post: { label: 'Post', bg: 'bg-blue-500/15 text-blue-400' },
  Both: { label: 'Pre+Post', bg: 'bg-purple-500/15 text-purple-400' },
}

export function AgentsListPage() {
  const navigate = useNavigate()
  const { data: agents, isLoading, error, refetch } = useAgents()
  const { data: funcs } = useFunctions()
  const deleteMutation = useDeleteAgent()

  const phaseMap = new Map(
    (funcs?.middlewareTypes ?? []).map((m) => [m.name, m.phase]),
  )

  const [deleteTarget, setDeleteTarget] = useState<AgentDef | null>(null)

  const columns: ColumnDef<AgentDef, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <span className="font-medium text-text-primary">{row.original.name}</span>
      ),
    },
    {
      accessorFn: (r) => r.model.deploymentName,
      id: 'model',
      header: 'Model',
      cell: ({ getValue }) => (
        <Badge variant="blue">{getValue() as string}</Badge>
      ),
    },
    {
      accessorFn: (r) => r.provider?.type ?? '-',
      id: 'provider',
      header: 'Provider',
    },
    {
      id: 'middlewares',
      header: 'Middlewares',
      cell: ({ row }) => {
        const mws = row.original.middlewares?.filter((m) => m.enabled !== false) ?? []
        if (mws.length === 0) return <span className="text-text-dimmed">-</span>
        return (
          <div className="flex flex-wrap gap-1">
            {mws.map((m) => {
              const phase = phaseMap.get(m.type)
              const style = phase ? PHASE_STYLE[phase] : undefined
              return (
                <span
                  key={m.type}
                  className={`text-[11px] font-medium px-1.5 py-0.5 rounded ${style?.bg ?? 'bg-bg-tertiary text-text-muted'}`}
                  title={style ? `Fase: ${style.label}` : m.type}
                >
                  {m.type}{style ? ` · ${style.label}` : ''}
                </span>
              )
            })}
          </div>
        )
      },
    },
    {
      id: 'version',
      header: 'Version',
      cell: ({ row }) => (
        <ActivePromptBadge agentId={row.original.id} />
      ),
    },
    {
      accessorFn: () => 'Running',
      id: 'status',
      header: 'Status',
      cell: () => (
        <Badge variant="green">Active</Badge>
      ),
    },
    {
      accessorKey: 'updatedAt',
      header: 'Last Execution',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return v ? new Date(v).toLocaleString('pt-BR') : '-'
      },
    },
    {
      id: 'cost',
      header: 'Cost 24h',
      cell: () => <span className="text-text-muted">-</span>,
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => navigate(`/agents/${row.original.id}/sandbox`)}
          >
            Sandbox
          </Button>
          <Button
            variant="danger"
            size="sm"
            onClick={() => setDeleteTarget(row.original)}
          >
            Delete
          </Button>
        </div>
      ),
    },
  ]

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar agentes." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Agentes</h1>
          <p className="text-sm text-text-muted mt-1">Gerencie os agentes de IA da plataforma.</p>
        </div>
        <Link to="/agents/new">
          <Button>Criar Agente</Button>
        </Link>
      </div>

      {/* Table */}
      <DataTable
        data={agents ?? []}
        columns={columns}
        searchPlaceholder="Buscar agente por nome..."
        onRowClick={(row) => navigate(`/agents/${row.id}`)}
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
        title="Excluir Agente"
        message={`Tem certeza que deseja excluir o agente "${deleteTarget?.name}"? Esta acao nao pode ser desfeita.`}
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteMutation.isPending}
      />
    </div>
  )
}
