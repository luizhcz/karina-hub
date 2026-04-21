import { useState } from 'react'
import { useNavigate } from 'react-router'
import { type ColumnDef } from '@tanstack/react-table'
import { DataTable } from '../../shared/data/DataTable'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { Input } from '../../shared/ui/Input'
import { Select } from '../../shared/ui/Select'
import { Card } from '../../shared/ui/Card'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAdminConversations, type ConversationSession } from '../../api/admin'
import { PAGE_SIZE_OPTIONS_STANDARD } from '../../constants/pagination'

export function ConversationListPage() {
  const navigate = useNavigate()

  const [userId, setUserId] = useState('')
  const [workflowId, setWorkflowId] = useState('')
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [pageSize, setPageSize] = useState('20')
  const [page, setPage] = useState(1)

  const params = {
    userId: userId || undefined,
    workflowId: workflowId || undefined,
    from: from || undefined,
    to: to || undefined,
    page,
    pageSize: Number(pageSize),
  }

  const { data, isLoading, error, refetch } = useAdminConversations(params)

  const conversations = data?.items ?? []
  const total = data?.total ?? 0
  const totalPages = Math.ceil(total / Number(pageSize))

  const columns: ColumnDef<ConversationSession, unknown>[] = [
    {
      accessorKey: 'conversationId',
      header: 'Conversation ID',
      cell: ({ getValue }) => {
        const id = getValue() as string
        return (
          <span className="font-mono text-xs text-text-secondary">
            {id.slice(0, 8)}…
          </span>
        )
      },
    },
    {
      accessorKey: 'userId',
      header: 'User ID',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-secondary">{getValue() as string}</span>
      ),
    },
    {
      accessorKey: 'workflowId',
      header: 'Workflow',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-secondary">{getValue() as string}</span>
      ),
    },
    {
      accessorKey: 'title',
      header: 'Título',
      cell: ({ getValue }) => {
        const title = getValue() as string | undefined
        return (
          <span className="text-sm text-text-secondary">{title ?? '—'}</span>
        )
      },
    },
    {
      accessorKey: 'lastMessageAt',
      header: 'Última Mensagem',
      cell: ({ getValue }) => (
        <span className="text-sm text-text-muted">
          {new Date(getValue() as string).toLocaleString('pt-BR')}
        </span>
      ),
    },
    {
      id: 'activeExecution',
      header: 'Execução Ativa',
      cell: ({ row }) => {
        const execId = row.original.activeExecutionId
        return execId ? (
          <Badge variant="blue">{execId.slice(0, 8)}…</Badge>
        ) : (
          <Badge variant="gray">—</Badge>
        )
      },
    },
  ]

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar conversas." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-text-primary">Conversas</h1>
        <p className="text-sm text-text-muted mt-1">
          Visão administrativa de todas as conversas.
        </p>
      </div>

      {/* Filters */}
      <Card title="Filtros">
        <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-3">
          <Input
            label="User ID"
            value={userId}
            onChange={(e) => { setUserId(e.target.value); setPage(1) }}
            placeholder="Filtrar por user"
          />
          <Input
            label="Workflow ID"
            value={workflowId}
            onChange={(e) => { setWorkflowId(e.target.value); setPage(1) }}
            placeholder="Filtrar por workflow"
          />
          <Input
            label="De"
            value={from}
            onChange={(e) => { setFrom(e.target.value); setPage(1) }}
            placeholder="YYYY-MM-DD"
          />
          <Input
            label="Até"
            value={to}
            onChange={(e) => { setTo(e.target.value); setPage(1) }}
            placeholder="YYYY-MM-DD"
          />
          <Select
            label="Por página"
            value={pageSize}
            onChange={(e) => { setPageSize(e.target.value); setPage(1) }}
            options={PAGE_SIZE_OPTIONS_STANDARD}
          />
        </div>
      </Card>

      {/* Table */}
      <DataTable
        data={conversations}
        columns={columns}
        searchPlaceholder="Buscar conversa..."
        onRowClick={(row) => navigate(`/chat/${row.conversationId}`)}
      />

      {/* Manual Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between text-sm text-text-muted">
          <span>
            {total} resultado(s) — Página {page} de {totalPages}
          </span>
          <div className="flex gap-2">
            <Button
              variant="secondary"
              size="sm"
              disabled={page <= 1}
              onClick={() => setPage((p) => Math.max(1, p - 1))}
            >
              Anterior
            </Button>
            <Button
              variant="secondary"
              size="sm"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            >
              Próxima
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
