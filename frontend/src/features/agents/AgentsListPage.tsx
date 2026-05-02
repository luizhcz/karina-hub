import { useState } from 'react'
import { Link, useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Tabs } from '../../shared/ui/Tabs'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgents, useDeleteAgent } from '../../api/agents'
import {
  useAgentDrafts,
  useDeleteAgentDraft,
  useCreateEditDraft,
  type AgentDraft,
} from '../../api/agentDrafts'
import type { AgentDef } from '../../api/agents'
import { ApiError } from '../../api/client'
import { useFunctions } from '../../api/tools'
import { toast } from '../../stores/toast'
import { ActivePromptBadge } from './components/ActivePromptBadge'

const PHASE_STYLE: Record<string, { label: string; bg: string }> = {
  Pre:  { label: 'Pre',  bg: 'bg-yellow-500/15 text-yellow-400' },
  Post: { label: 'Post', bg: 'bg-blue-500/15 text-blue-400' },
  Both: { label: 'Pre+Post', bg: 'bg-purple-500/15 text-purple-400' },
}

type Tab = 'published' | 'drafts'

export function AgentsListPage() {
  const navigate = useNavigate()
  const { data: agents, isLoading, error, refetch } = useAgents()
  const { data: drafts, isLoading: draftsLoading } = useAgentDrafts()
  const { data: funcs } = useFunctions()
  const deleteMutation = useDeleteAgent()
  const deleteDraftMutation = useDeleteAgentDraft()
  const createEditDraftMutation = useCreateEditDraft()

  const phaseMap = new Map(
    (funcs?.middlewareTypes ?? []).map((m) => [m.name, m.phase]),
  )

  const [tab, setTab] = useState<Tab>('published')
  const [draftStatusFilter, setDraftStatusFilter] = useState<'all' | 'Draft' | 'PendingApproval' | 'Rejected'>('all')
  const [deleteTarget, setDeleteTarget] = useState<AgentDef | null>(null)
  const [deleteDraftTarget, setDeleteDraftTarget] = useState<AgentDraft | null>(null)

  const filteredDrafts = (drafts ?? []).filter((d) =>
    draftStatusFilter === 'all' ? true : d.status === draftStatusFilter,
  )

  const handleEditAsDraft = (agentId: string) => {
    createEditDraftMutation.mutate(agentId, {
      onSuccess: (draft) => {
        toast.success('Rascunho de edição criado.')
        navigate(`/agents/drafts/${draft.id}`)
      },
      onError: (err) => {
        if (err instanceof ApiError && err.status === 409) {
          toast.error('Já existe rascunho de edição em andamento. Acesse a aba Rascunhos.')
          setTab('drafts')
          return
        }
        const msg = err instanceof ApiError ? err.message : 'Erro ao criar rascunho de edição.'
        toast.error(msg)
      },
    })
  }

  const publishedColumns: ColumnDef<AgentDef, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium text-text-primary">{row.original.name}</span>
          {row.original.visibility === 'global' && (
            <Badge variant="purple">
              🌐 {row.original.originProjectId
                ? `Global · ${row.original.originProjectId}`
                : 'Global'}
            </Badge>
          )}
        </div>
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
            onClick={() => handleEditAsDraft(row.original.id)}
            loading={createEditDraftMutation.isPending}
          >
            Editar como rascunho
          </Button>
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

  const draftColumns: ColumnDef<AgentDraft, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium text-text-primary">
            {row.original.name || row.original.id}
          </span>
          {row.original.isEditDraft && (
            <Badge variant="purple">Edit</Badge>
          )}
        </div>
      ),
    },
    {
      accessorKey: 'status',
      header: 'Status',
      cell: ({ row }) => {
        const s = row.original.status
        if (s === 'PendingApproval') return <Badge variant="blue">Em aprovação</Badge>
        if (s === 'Rejected') return <Badge variant="red">Rejeitado</Badge>
        return <Badge variant="yellow">Rascunho</Badge>
      },
    },
    {
      accessorKey: 'id',
      header: 'Id',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-muted">{getValue() as string}</span>
      ),
    },
    {
      accessorFn: (r) => r.payload.model?.deploymentName ?? '—',
      id: 'model',
      header: 'Model',
      cell: ({ getValue }) => {
        const v = getValue() as string
        return v === '—' ? <span className="text-text-dimmed">—</span> : <Badge variant="blue">{v}</Badge>
      },
    },
    {
      accessorKey: 'baseAgentId',
      header: 'Base agent',
      cell: ({ getValue, row }) => {
        const v = getValue() as string | null | undefined
        if (!v) return <span className="text-text-dimmed">—</span>
        return (
          <span className="font-mono text-xs text-text-muted">
            {v} {row.original.baseRevision !== null && row.original.baseRevision !== undefined
              ? `· r${row.original.baseRevision}`
              : ''}
          </span>
        )
      },
    },
    {
      accessorKey: 'updatedAt',
      header: 'Atualizado',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return v ? new Date(v).toLocaleString('pt-BR') : '-'
      },
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => (
        <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="danger"
            size="sm"
            onClick={() => setDeleteDraftTarget(row.original)}
          >
            Descartar
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

  const tabs = [
    { key: 'published', label: 'Publicados', badge: agents?.length ?? 0 },
    { key: 'drafts', label: 'Rascunhos', badge: drafts?.length ?? 0 },
  ]

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Agentes</h1>
          <p className="text-sm text-text-muted mt-1">Gerencie os agentes de IA da plataforma.</p>
        </div>
        <Link to="/agents/new">
          <Button>Criar Agente</Button>
        </Link>
      </div>

      <Tabs items={tabs} active={tab} onChange={(k) => setTab(k as Tab)} />

      {tab === 'published' && (
        <DataTable
          data={agents ?? []}
          columns={publishedColumns}
          searchPlaceholder="Buscar agente por nome..."
          onRowClick={(row) => navigate(`/agents/${row.id}`)}
        />
      )}

      {tab === 'drafts' && (
        draftsLoading ? (
          <PageLoader />
        ) : (
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2">
              {(['all', 'Draft', 'PendingApproval', 'Rejected'] as const).map((s) => {
                const label = s === 'all'
                  ? 'Todos'
                  : s === 'Draft' ? 'Em rascunho'
                  : s === 'PendingApproval' ? 'Em aprovação'
                  : 'Rejeitados'
                const count = s === 'all'
                  ? (drafts?.length ?? 0)
                  : (drafts ?? []).filter((d) => d.status === s).length
                const active = draftStatusFilter === s
                return (
                  <button
                    key={s}
                    type="button"
                    onClick={() => setDraftStatusFilter(s)}
                    className={`px-3 py-1 text-xs rounded-full border transition-colors ${
                      active
                        ? 'bg-accent-blue/20 border-accent-blue text-accent-blue'
                        : 'border-border-primary text-text-muted hover:text-text-secondary'
                    }`}
                  >
                    {label} <span className="ml-1 text-text-dimmed">{count}</span>
                  </button>
                )
              })}
            </div>
            <DataTable
              data={filteredDrafts}
              columns={draftColumns}
              searchPlaceholder="Buscar rascunho..."
              onRowClick={(row) => navigate(`/agents/drafts/${row.id}`)}
            />
          </div>
        )
      )}

      <ConfirmDialog
        open={!!deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (deleteTarget) {
            deleteMutation.mutate(deleteTarget.id, {
              onSuccess: () => setDeleteTarget(null),
              onError: (err) => {
                setDeleteTarget(null)
                const msg = err instanceof ApiError ? err.message : 'Erro ao excluir agente.'
                toast.error(msg)
              },
            })
          }
        }}
        title="Excluir Agente"
        message={`Tem certeza que deseja excluir o agente "${deleteTarget?.name}"? Esta acao nao pode ser desfeita.`}
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteMutation.isPending}
      />

      <ConfirmDialog
        open={!!deleteDraftTarget}
        onClose={() => setDeleteDraftTarget(null)}
        onConfirm={() => {
          if (deleteDraftTarget) {
            deleteDraftMutation.mutate(deleteDraftTarget.id, {
              onSuccess: () => setDeleteDraftTarget(null),
              onError: (err) => {
                setDeleteDraftTarget(null)
                const msg = err instanceof ApiError ? err.message : 'Erro ao descartar rascunho.'
                toast.error(msg)
              },
            })
          }
        }}
        title="Descartar rascunho"
        message={`Descartar o rascunho "${deleteDraftTarget?.name || deleteDraftTarget?.id}"?`}
        confirmLabel="Descartar"
        variant="danger"
        loading={deleteDraftMutation.isPending}
      />
    </div>
  )
}
