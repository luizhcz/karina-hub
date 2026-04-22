import { Link, useNavigate } from 'react-router'
import { useCreateMcpServer } from '../../api/mcpServers'
import { Button } from '../../shared/ui/Button'
import { ApiError } from '../../api/client'
import { McpServerForm } from './McpServerForm'

export function McpServerCreatePage() {
  const navigate = useNavigate()
  const createMcp = useCreateMcpServer()

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center gap-3">
        <Link to="/mcp-servers">
          <Button variant="ghost" size="sm">← MCP Servers</Button>
        </Link>
        <h1 className="text-2xl font-bold text-text-primary">Novo MCP Server</h1>
      </div>

      <McpServerForm
        loading={createMcp.isPending}
        submitLabel="Criar"
        onSubmit={(payload) => {
          createMcp.mutate(payload, {
            onSuccess: () => navigate('/mcp-servers'),
          })
        }}
      />

      {createMcp.isError && (
        <div className="px-4 py-3 rounded-lg text-sm bg-red-500/10 border border-red-500/30 text-red-400">
          {createMcp.error instanceof ApiError ? createMcp.error.message : 'Erro ao criar MCP server.'}
        </div>
      )}
    </div>
  )
}
