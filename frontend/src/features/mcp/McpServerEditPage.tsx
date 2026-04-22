import { Link, useNavigate, useParams } from 'react-router'
import { useMcpServer, useUpdateMcpServer } from '../../api/mcpServers'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { ApiError } from '../../api/client'
import { McpServerForm } from './McpServerForm'

export function McpServerEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: server, isLoading, error, refetch } = useMcpServer(id!, !!id)
  const updateMcp = useUpdateMcpServer()

  if (isLoading) return <PageLoader />
  if (error || !server) return <ErrorCard message="Erro ao carregar MCP server." onRetry={refetch} />

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center gap-3">
        <Link to="/mcp-servers">
          <Button variant="ghost" size="sm">← MCP Servers</Button>
        </Link>
        <h1 className="text-2xl font-bold text-text-primary">{server.name}</h1>
        <span className="font-mono text-xs text-text-dimmed">{server.id}</span>
      </div>

      <McpServerForm
        initial={server}
        idLocked
        loading={updateMcp.isPending}
        submitLabel="Salvar alterações"
        onSubmit={(payload) => {
          updateMcp.mutate(
            { id: id!, body: payload },
            { onSuccess: () => navigate('/mcp-servers') },
          )
        }}
      />

      {updateMcp.isError && (
        <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
          {updateMcp.error instanceof ApiError ? updateMcp.error.message : 'Erro ao atualizar MCP server.'}
        </div>
      )}
    </div>
  )
}
