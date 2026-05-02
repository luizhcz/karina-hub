import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useGenericTools, useDeleteGenericTool } from '../../api/genericTools'
import type { GenericTool } from '../../api/genericTools'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { ApiError } from '../../api/client'

export function GenericToolsListPage() {
  const navigate = useNavigate()
  const { data: tools, isLoading, error, refetch } = useGenericTools()
  const deleteTool = useDeleteGenericTool()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error instanceof ApiError && error.status === 403) {
    return <ErrorCard message={error.message} onRetry={refetch} />
  }
  if (error) return <ErrorCard message="Erro ao carregar Generic Tools" onRetry={refetch} />

  const items = tools ?? []

  const columns: ColumnDef<GenericTool, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'httpMethod',
      header: 'Método',
      cell: ({ getValue }) => (
        <Badge variant={getValue() === 'GET' ? 'blue' : 'green'}>{String(getValue())}</Badge>
      ),
    },
    {
      accessorKey: 'urlTemplate',
      header: 'URL',
      cell: ({ getValue }) => {
        const v = getValue() as string
        const truncated = v.length > 60 ? v.slice(0, 60) + '…' : v
        return (
          <code className="text-xs text-text-muted font-mono">{truncated}</code>
        )
      },
    },
    {
      accessorKey: 'outputContentType',
      header: 'Output',
      cell: ({ getValue }) => (
        <Badge variant="purple">{String(getValue())}</Badge>
      ),
    },
    {
      accessorKey: 'updatedAt',
      header: 'Atualizado',
      cell: ({ getValue }) => {
        const v = getValue() as string | undefined
        return (
          <span className="text-xs text-text-muted">
            {v ? new Date(v).toLocaleDateString('pt-BR') : '—'}
          </span>
        )
      },
    },
    {
      id: 'actions',
      header: 'Ações',
      cell: ({ row }) => (
        <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/generic-tools/${row.original.id}`)}
          >
            Editar
          </Button>
          <Button
            variant="danger"
            size="sm"
            onClick={() => setDeletingId(row.original.id)}
          >
            Excluir
          </Button>
        </div>
      ),
    },
  ]

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Generic Tools</h1>
          <p className="text-sm text-text-muted mt-1">
            {items.length} tool(s) cadastrada(s) — exclusivo do projeto atual.
          </p>
        </div>
        <Button onClick={() => navigate('/generic-tools/new')}>Novo Tool</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum Generic Tool cadastrado"
            description="Cadastre tools HTTP genéricas (GET/POST) para que seus agentes possam chamar APIs externas."
            action={<Button onClick={() => navigate('/generic-tools/new')}>Novo Tool</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar tool..."
            onRowClick={(row) => navigate(`/generic-tools/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId) {
            deleteTool.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir Generic Tool"
        message="Tem certeza que deseja excluir este tool? Agents que o referenciam perderão acesso. Esta ação não pode ser desfeita."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteTool.isPending}
      />
    </div>
  )
}
