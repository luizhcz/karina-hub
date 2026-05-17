import { useEffect, useState } from 'react'
import { Link } from 'react-router'
import { useIsAdmin } from '../../stores/me'
import { friendlyError } from '../../api/client'
import {
  listLlmCalls,
  type LlmCallSummary,
} from '../../api/admin/llmCalls'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  Input,
  Select,
  Spinner,
} from '../../ui'

const PAGE_SIZE = 50

function statusTone(status: string): 'neutral' | 'success' | 'warning' | 'danger' {
  if (status === 'Completed') return 'success'
  if (status === 'Failed') return 'danger'
  return 'neutral'
}

function formatDate(iso: string): string {
  try {
    return new Date(iso).toLocaleString('pt-BR', {
      day: '2-digit', month: '2-digit', year: 'numeric',
      hour: '2-digit', minute: '2-digit', second: '2-digit',
    })
  } catch {
    return iso
  }
}

export function LlmCallsList() {
  const isAdmin = useIsAdmin()
  const [items, setItems] = useState<LlmCallSummary[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Filtros
  const [agentId, setAgentId] = useState('')
  const [intent, setIntent] = useState('')
  const [status, setStatus] = useState('')
  const [executionId, setExecutionId] = useState('')

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE))

  const reload = async (targetPage = page) => {
    setLoading(true)
    setError(null)
    try {
      const resp = await listLlmCalls({
        agentId: agentId.trim() || undefined,
        intent: intent.trim() || undefined,
        status: status.trim() || undefined,
        executionId: executionId.trim() || undefined,
        page: targetPage,
        size: PAGE_SIZE,
      })
      setItems(resp.items)
      setTotal(resp.total)
      setPage(resp.page)
    } catch (err) {
      setError(friendlyError(err, 'Falha ao listar chamadas LLM.'))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    if (isAdmin !== false) void reload(1)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAdmin])

  if (isAdmin === false) {
    return (
      <Card padded className="mx-auto max-w-3xl text-center">
        <p className="text-sm text-fg-muted">Acesso restrito a administradores.</p>
      </Card>
    )
  }

  return (
    <div className="mx-auto max-w-7xl">
      <div className="mb-6 flex items-end justify-between gap-4">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Chamadas LLM Capturadas</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Para popular esta lista, ligue a captura em{' '}
            <Link to="/admin/llm-capture" className="text-accent underline">
              Captura de Prompts LLM
            </Link>
            .
          </p>
        </div>
        <Button variant="secondary" size="sm" onClick={() => void reload(page)}>
          Atualizar
        </Button>
      </div>

      <Card padded className="mb-4 space-y-3">
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-4">
          <Input
            label="Agente"
            placeholder="router-atendimento-cliente"
            value={agentId}
            onChange={(e) => setAgentId(e.target.value)}
          />
          <Input
            label="Intent"
            placeholder="out_of_scope"
            value={intent}
            onChange={(e) => setIntent(e.target.value)}
          />
          <Select
            label="Status"
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            options={[
              { value: '', label: 'Todos' },
              { value: 'Completed', label: 'Completed' },
              { value: 'Failed', label: 'Failed' },
            ]}
          />
          <Input
            label="ExecutionId"
            placeholder="..."
            value={executionId}
            onChange={(e) => setExecutionId(e.target.value)}
          />
        </div>
        <div className="flex justify-end gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => {
              setAgentId('')
              setIntent('')
              setStatus('')
              setExecutionId('')
              void reload(1)
            }}
          >
            Limpar
          </Button>
          <Button size="sm" onClick={() => void reload(1)}>
            Buscar
          </Button>
        </div>
      </Card>

      {error && <ErrorMessage message={error} />}

      {loading ? (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      ) : items.length === 0 ? (
        <EmptyState
          title="Nenhuma chamada capturada"
          description="A captura está OFF ou os filtros não casaram. Verifique em Captura de Prompts LLM."
        />
      ) : (
        <Card>
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="border-b border-border text-[10px] uppercase tracking-wider text-fg-dim">
                  <th className="px-3 py-2 text-left font-semibold">Quando</th>
                  <th className="px-3 py-2 text-left font-semibold">Agent</th>
                  <th className="px-3 py-2 text-left font-semibold">Intent</th>
                  <th className="px-3 py-2 text-left font-semibold">Provider/Model</th>
                  <th className="px-3 py-2 text-right font-semibold">Tokens (in/out)</th>
                  <th className="px-3 py-2 text-right font-semibold">Duração</th>
                  <th className="px-3 py-2 text-center font-semibold">Status</th>
                  <th className="px-3 py-2"></th>
                </tr>
              </thead>
              <tbody>
                {items.map((row) => (
                  <tr key={row.id} className="border-b border-border last:border-b-0 hover:bg-bg-soft">
                    <td className="px-3 py-2 font-mono text-fg-muted">{formatDate(row.createdAt)}</td>
                    <td className="px-3 py-2">
                      <span className="font-mono">{row.agentId}</span>
                      {row.attemptIndex > 0 && (
                        <Badge tone="warning" className="ml-1">
                          attempt {row.attemptIndex}
                        </Badge>
                      )}
                    </td>
                    <td className="px-3 py-2 font-mono">{row.intent ?? '—'}</td>
                    <td className="px-3 py-2 font-mono text-fg-muted">
                      {row.providerResolved}/{row.model}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">
                      {row.inputTokens}/{row.outputTokens}
                    </td>
                    <td className="px-3 py-2 text-right font-mono">{Math.round(row.durationMs)}ms</td>
                    <td className="px-3 py-2 text-center">
                      <Badge tone={statusTone(row.status)}>{row.status}</Badge>
                      {row.truncated && (
                        <Badge tone="warning" className="ml-1">
                          truncated
                        </Badge>
                      )}
                    </td>
                    <td className="px-3 py-2 text-right">
                      <Link
                        to={`/admin/llm-calls/${row.id}`}
                        className="text-accent hover:underline"
                      >
                        ver →
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-between border-t border-border px-3 py-2 text-xs">
              <span className="text-fg-muted">
                Página {page} de {totalPages} ({total} total)
              </span>
              <div className="flex gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={page <= 1}
                  onClick={() => void reload(page - 1)}
                >
                  Anterior
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  disabled={page >= totalPages}
                  onClick={() => void reload(page + 1)}
                >
                  Próxima
                </Button>
              </div>
            </div>
          )}
        </Card>
      )}
    </div>
  )
}
