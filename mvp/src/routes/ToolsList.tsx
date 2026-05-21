import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { listGenericTools, type GenericTool } from '../api/genericTools'
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
  Spinner,
  ToolIcon,
} from '../ui'

export function ToolsList() {
  const navigate = useNavigate()
  const [tools, setTools] = useState<GenericTool[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    listGenericTools()
      .then((list) => {
        if (!cancelled) setTools(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as ferramentas.'))
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
    if (!q) return tools
    return tools.filter(
      (t) =>
        t.name.toLowerCase().includes(q) ||
        t.urlTemplate.toLowerCase().includes(q),
    )
  }, [tools, search])

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Ferramentas</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Endpoints HTTP que seus agentes podem chamar. Crie uma vez e reutilize em qualquer fluxo.
          </p>
        </div>
        <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/ferramentas/nova')}>
          Nova ferramenta
        </Button>
      </div>

      <div className="mb-6 max-w-md">
        <Input
          placeholder="Buscar por nome, descrição ou URL…"
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
            icon={<ToolIcon className="h-6 w-6" />}
            title={tools.length === 0 ? 'Nenhuma ferramenta ainda' : 'Nada bate com a busca'}
            description={
              tools.length === 0
                ? 'Crie sua primeira ferramenta apontando para um endpoint HTTP que seu agente vai consumir.'
                : 'Tente ajustar o termo de busca.'
            }
            action={
              tools.length === 0 ? (
                <Button leftIcon={<PlusIcon className="h-4 w-4" />} onClick={() => navigate('/ferramentas/nova')}>
                  Criar ferramenta
                </Button>
              ) : null
            }
          />
        </Card>
      )}

      {!loading && !error && filtered.length > 0 && (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {filtered.map((t) => (
            <Card
              key={t.id}
              interactive
              role="button"
              tabIndex={0}
              onClick={() => navigate(`/ferramentas/${t.id}`)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' || e.key === ' ') {
                  e.preventDefault()
                  navigate(`/ferramentas/${t.id}`)
                }
              }}
              className="group flex cursor-pointer flex-col items-start gap-3"
            >
              <div className="flex w-full items-start justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <h3 className="truncate text-sm font-semibold text-fg">{t.name}</h3>
                </div>
                <Badge tone={t.httpMethod === 'GET' ? 'method-get' : 'method-post'}>
                  {t.httpMethod}
                </Badge>
              </div>

              <code className="w-full truncate rounded-md bg-bg-soft px-2 py-1.5 font-mono text-[11px] text-fg-muted">
                {t.urlTemplate}
              </code>

              <div className="mt-auto flex w-full items-center justify-between text-[11px] text-fg-dim">
                <div className="flex items-center gap-2">
                  <Badge>in: {t.inputContentType}</Badge>
                  <Badge>out: {t.outputContentType}</Badge>
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
