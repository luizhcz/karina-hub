import { useState } from 'react'
import { useNavigate } from 'react-router'
import type { ColumnDef } from '@tanstack/react-table'
import { useMcpServers, useDeleteMcpServer } from '../../api/mcpServers'
import type { McpServer } from '../../api/mcpServers'
import { DataTable } from '../../shared/data/DataTable'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'

export function McpServersListPage() {
  const navigate = useNavigate()
  const { data: servers, isLoading, error, refetch } = useMcpServers()
  const deleteMcp = useDeleteMcpServer()
  const [deletingId, setDeletingId] = useState<string | null>(null)

  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar MCP servers" onRetry={refetch} />

  const items = servers ?? []

  const columns: ColumnDef<McpServer, unknown>[] = [
    {
      accessorKey: 'name',
      header: 'Nome',
      cell: ({ getValue }) => (
        <span className="font-medium text-text-primary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'serverLabel',
      header: 'Label',
      cell: ({ getValue }) => <Badge variant="purple">{String(getValue())}</Badge>,
    },
    {
      accessorKey: 'serverUrl',
      header: 'URL',
      cell: ({ getValue }) => (
        <span className="font-mono text-xs text-text-secondary">{String(getValue())}</span>
      ),
    },
    {
      accessorKey: 'allowedTools',
      header: 'Tools',
      cell: ({ getValue }) => {
        const v = (getValue() as string[]) ?? []
        return <Badge variant="gray">{v.length}</Badge>
      },
    },
    {
      accessorKey: 'requireApproval',
      header: 'Aprovação',
      cell: ({ getValue }) => {
        const v = String(getValue())
        return <Badge variant={v === 'always' ? 'yellow' : 'gray'}>{v}</Badge>
      },
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
            onClick={() => navigate(`/mcp-servers/${row.original.id}`)}
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
          <h1 className="text-2xl font-bold text-text-primary">MCP Servers</h1>
          <p className="text-sm text-text-muted mt-1">
            {items.length} servidor(es) registrado(s). Agents referenciam por Id — alterações
            aqui propagam automaticamente para os agentes que usam cada MCP.
          </p>
        </div>
        <Button onClick={() => navigate('/mcp-servers/new')}>Novo MCP Server</Button>
      </div>

      <Card padding={false}>
        {items.length === 0 ? (
          <EmptyState
            title="Nenhum MCP server registrado"
            description="Cadastre servidores MCP (Model Context Protocol) para que os agentes possam consumi-los."
            action={<Button onClick={() => navigate('/mcp-servers/new')}>Novo MCP Server</Button>}
          />
        ) : (
          <DataTable
            data={items}
            columns={columns}
            searchPlaceholder="Buscar MCP server..."
            onRowClick={(row) => navigate(`/mcp-servers/${row.id}`)}
          />
        )}
      </Card>

      <ConfirmDialog
        open={deletingId !== null}
        onClose={() => setDeletingId(null)}
        onConfirm={() => {
          if (deletingId) {
            deleteMcp.mutate(deletingId, { onSuccess: () => setDeletingId(null) })
          }
        }}
        title="Excluir MCP Server"
        message="Remover este MCP server. Agentes que referenciam este Id ficarão com a tool pulada (dangling) — a execução não falha, mas a tool não estará disponível até você recriar o registro."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteMcp.isPending}
      />
    </div>
  )
}
