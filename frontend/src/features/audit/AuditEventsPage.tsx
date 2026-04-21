import { useState } from 'react'
import type { ColumnDef } from '@tanstack/react-table'
import { useAdminConversations } from '../../api/admin'
import type { ConversationSession } from '../../api/admin'
import { useProjects } from '../../api/projects'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Modal } from '../../shared/ui/Modal'
import { JsonViewer } from '../../shared/data/JsonViewer'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { PAGE_SIZE_OPTIONS_AUDIT } from '../../constants/pagination'

export function AuditEventsPage() {
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [pageSize, setPageSize] = useState('25')
  const [projectId, setProjectId] = useState('')
  const [selected, setSelected] = useState<ConversationSession | null>(null)

  const { data: projectsData } = useProjects()
  const projects = projectsData ?? []

  const { data, isLoading, error, refetch } = useAdminConversations({
    from: from || undefined,
    to: to || undefined,
    projectId: projectId || undefined,
    pageSize: Number(pageSize),
  })

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar eventos de auditoria" onRetry={refetch} />

  const items = data?.items ?? []

  const columns: ColumnDef<ConversationSession, unknown>[] = [
    {
      accessorKey: 'conversationId',
      header: 'Conversation ID',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue()).slice(0, 16)}…</span>
      ),
    },
    {
      accessorKey: 'userId',
      header: 'User ID',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'workflowId',
      header: 'Workflow',
      cell: ({ getValue }) => (
        <Badge variant="blue">{String(getValue()).slice(0, 20)}</Badge>
      ),
    },
    {
      accessorKey: 'userType',
      header: 'Tipo',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return v ? <Badge variant="gray">{v}</Badge> : <span className="text-text-dimmed">—</span>
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
      accessorKey: 'lastMessageAt',
      header: 'Última msg',
      cell: ({ getValue }) => (
        <span className="text-xs text-text-muted">{new Date(String(getValue())).toLocaleString('pt-BR')}</span>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Audit Events</h1>
          <p className="text-sm text-text-muted mt-1">Histórico de conversações e eventos</p>
        </div>
      </div>

      <Card title="Filtros">
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
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
          <div className="flex flex-col gap-1">
            <label className="text-xs text-text-muted">Projeto</label>
            <select
              value={projectId}
              onChange={(e) => setProjectId(e.target.value)}
              className="bg-bg-tertiary border border-border-secondary rounded-md px-2.5 py-1.5 text-sm text-text-primary focus:outline-none focus:border-accent-blue"
            >
              <option value="">Todos os projetos</option>
              {projects.map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </select>
          </div>
          <Select
            label="Itens por página"
            value={pageSize}
            onChange={(e) => setPageSize(e.target.value)}
            options={PAGE_SIZE_OPTIONS_AUDIT}
          />
        </div>
      </Card>

      <Card title={`Eventos (${items.length})`} padding={false}>
        {items.length === 0 ? (
          <EmptyState title="Nenhum evento" description="Nenhum evento de auditoria encontrado neste período." />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar evento..."
            onRowClick={(row) => setSelected(row)}
          />
        )}
      </Card>

      <Modal
        open={selected !== null}
        onClose={() => setSelected(null)}
        title="Detalhes do Evento"
        size="lg"
      >
        {selected && (
          <div className="flex flex-col gap-4">
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div>
                <span className="text-text-muted">Conversation ID:</span>
                <p className="font-mono text-xs text-text-primary mt-1">{selected.conversationId}</p>
              </div>
              <div>
                <span className="text-text-muted">User ID:</span>
                <p className="text-text-primary mt-1">{selected.userId}</p>
              </div>
              <div>
                <span className="text-text-muted">Workflow ID:</span>
                <p className="font-mono text-xs text-text-primary mt-1">{selected.workflowId}</p>
              </div>
              <div>
                <span className="text-text-muted">Tipo:</span>
                <p className="text-text-primary mt-1">{selected.userType ?? '—'}</p>
              </div>
            </div>
            {selected.metadata && (
              <div>
                <p className="text-sm text-text-muted mb-2">Metadata:</p>
                <JsonViewer data={selected.metadata} collapsed={false} />
              </div>
            )}
          </div>
        )}
      </Modal>
    </div>
  )
}
