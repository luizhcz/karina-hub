import { useEffect, useMemo, useState } from 'react'
import { listAgents, type Agent } from '../../api/agents'
import { listAgentVersions } from '../../api/agentVersions'
import { friendlyError } from '../../api/client'
import {
  AgentIcon,
  Badge,
  ErrorMessage,
  Input,
  Modal,
  SearchIcon,
  Spinner,
  cn,
} from '../../ui'

export interface PickedAgent {
  agent: Agent
  /** Versão atual pinada — backend exige pin no save do workflow. */
  agentVersionId: string
}

interface AgentPickerModalProps {
  open: boolean
  onClose: () => void
  /** IDs já presentes na sequência. Marca duplicados como bloqueados. */
  excludeIds: ReadonlySet<string>
  onPick: (picked: PickedAgent) => void
}

/**
 * Modal de seleção de agente publicado para inserir num pipeline. Lista
 * agentes com `enabled !== false`. Agentes já presentes na sequência aparecem
 * desabilitados pra evitar a regra do backend que rejeita duplicados.
 */
export function AgentPickerModal({ open, onClose, excludeIds, onPick }: AgentPickerModalProps) {
  const [agents, setAgents] = useState<Agent[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [search, setSearch] = useState('')

  useEffect(() => {
    if (!open) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listAgents()
      .then((list) => {
        if (!cancelled) setAgents(list.filter((a) => a.enabled !== false))
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar os agentes publicados.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [open])

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase()
    if (!q) return agents
    return agents.filter((a) => {
      const name = (a.name ?? '').toLowerCase()
      const desc = (a.description ?? '').toLowerCase()
      return name.includes(q) || desc.includes(q) || a.id.toLowerCase().includes(q)
    })
  }, [agents, search])

  const [resolving, setResolving] = useState<string | null>(null)
  const [pickError, setPickError] = useState<string | null>(null)

  // Backend exige `AgentVersionId` no save do workflow — resolvemos a versão
  // current na hora do pick. Se o agente nunca foi versionado (lista vazia), o
  // botão fica em erro com instrução clara, sem deixar o user salvar e bater
  // numa rejeição genérica do workflow validator depois.
  const handlePick = async (agent: Agent) => {
    setPickError(null)
    setResolving(agent.id)
    try {
      const versions = await listAgentVersions(agent.id)
      if (versions.length === 0) {
        setPickError(
          `O agente "${agent.name}" ainda não foi versionado. Publique uma revisão antes de adicionar ao pipeline.`,
        )
        return
      }
      const current = versions[0]
      onPick({ agent, agentVersionId: current.agentVersionId })
      setSearch('')
    } catch (err) {
      setPickError(friendlyError(err, 'Não foi possível resolver a versão do agente.'))
    } finally {
      setResolving(null)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title="Adicionar agente à sequência"
      description="O agente escolhido entra no fim da fila — você pode reordenar depois com as setas."
    >
      <div className="space-y-3">
        <Input
          placeholder="Buscar por nome, descrição ou id…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
        {loading && (
          <div className="flex items-center justify-center py-8">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        )}
        {!loading && error && <ErrorMessage message={error} />}
        {pickError && <ErrorMessage message={pickError} />}
        {!loading && !error && filtered.length === 0 && (
          <p className="py-6 text-center text-sm text-fg-muted">
            Nenhum agente publicado disponível.
          </p>
        )}
        {!loading && !error && filtered.length > 0 && (
          <ul className="max-h-[400px] space-y-1.5 overflow-y-auto">
            {filtered.map((a) => {
              const already = excludeIds.has(a.id)
              return (
                <li key={a.id}>
                  <button
                    type="button"
                    onClick={() => !already && handlePick(a)}
                    disabled={already || resolving !== null}
                    className={cn(
                      'flex w-full items-center gap-3 rounded-lg border border-border bg-surface px-3 py-2.5 text-left transition',
                      already || resolving === a.id
                        ? 'cursor-not-allowed opacity-60'
                        : 'hover:border-accent/60 hover:bg-accent-subtle/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
                    )}
                  >
                    {resolving === a.id && (
                      <Spinner className="h-4 w-4 text-fg-muted" />
                    )}
                    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-accent-subtle text-accent">
                      <AgentIcon className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span className="truncate text-sm font-semibold text-fg">{a.name}</span>
                        {already && <Badge tone="warning">já está na sequência</Badge>}
                      </div>
                      {a.description && (
                        <p className="mt-0.5 line-clamp-1 text-xs text-fg-muted">{a.description}</p>
                      )}
                      <p className="mt-0.5 truncate font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                        {a.id}
                      </p>
                    </div>
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </Modal>
  )
}
