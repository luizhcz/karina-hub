import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { listMcpServers, type McpServer } from '../api/mcpServers'
import { friendlyError } from '../api/client'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  Input,
  PlusIcon,
  SearchIcon,
  ServerIcon,
  Spinner,
} from '../ui'

export function McpServersList() {
  const navigate = useNavigate()
  const [items, setItems] = useState<McpServer[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    listMcpServers()
      .then((list) => {
        if (!cancelled) setItems(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar os MCPs.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return items
    return items.filter(
      (m) =>
        m.name.toLowerCase().includes(q) ||
        m.serverLabel.toLowerCase().includes(q) ||
        m.serverUrl.toLowerCase().includes(q) ||
        (m.description ?? '').toLowerCase().includes(q),
    )
  }, [items, search])

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">MCPs</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Servidores Model Context Protocol que seus agentes podem consumir. O agente referencia
            pelo Id e o runtime resolve label, URL e tools permitidas em cada execução.
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/mcps/novo')}>
          Novo MCP
        </Button>
      </div>

      <div className="mb-6 max-w-md">
        <Input
          placeholder="Buscar por nome, label ou URL…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
      </div>

      {loading && (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      )}

      {error && <ErrorMessage message={error} />}

      {!loading && !error && filtered.length === 0 && (
        <Card padded={false}>
          <EmptyState
            icon={<ServerIcon className="h-6 w-6" />}
            title={items.length === 0 ? 'Nenhum MCP cadastrado' : 'Nada bate com a busca'}
            description={
              items.length === 0
                ? 'Cadastre um servidor MCP pra que seus agentes possam invocar suas tools em runtime.'
                : 'Tente ajustar o termo de busca.'
            }
            action={
              items.length === 0 ? (
                <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/mcps/novo')}>
                  Cadastrar MCP
                </Button>
              ) : null
            }
          />
        </Card>
      )}

      {!loading && !error && filtered.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {filtered.map((m) => (
            <Card
              key={m.id}
              interactive
              role="button"
              tabIndex={0}
              onClick={() => navigate(`/mcps/${m.id}`)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  navigate(`/mcps/${m.id}`)
                }
              }}
              className="group flex cursor-pointer flex-col items-start gap-3"
            >
              <div className="flex w-full items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <h3 className="truncate text-sm font-semibold text-fg">{m.name}</h3>
                  <p className="mt-1 line-clamp-2 text-xs text-fg-muted">
                    {m.description || <span className="italic text-fg-dim">sem descrição</span>}
                  </p>
                </div>
                <Badge tone="accent" className="font-mono">{m.serverLabel}</Badge>
              </div>

              <code className="w-full truncate rounded-md bg-bg-soft px-2 py-1.5 font-mono text-[11px] text-fg-muted">
                {m.serverUrl}
              </code>

              <div className="mt-auto flex w-full items-center justify-between text-[11px] text-fg-dim">
                <div className="flex items-center gap-2">
                  <Badge>
                    {m.allowedTools.length} {m.allowedTools.length === 1 ? 'tool' : 'tools'}
                  </Badge>
                  <Badge tone={m.requireApproval === 'always' ? 'warning' : 'success'}>
                    {m.requireApproval === 'always' ? 'aprovação humana' : 'sem HITL'}
                  </Badge>
                </div>
                <span className="opacity-0 transition group-hover:opacity-100">Editar →</span>
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
