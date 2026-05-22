import { useMemo, useState } from 'react'
import {
  updateAgentFull,
  type Agent,
  type AgentMiddleware,
  type UpdateAgentFullBody,
} from '../../api/agents'
import { friendlyError } from '../../api/client'
import {
  defaultSettingsFor,
  type MiddlewareTypeInfo,
} from '../../api/middlewares'
import {
  Badge,
  Button,
  ErrorMessage,
  Input,
  Modal,
  SearchIcon,
  Spinner,
  cn,
} from '../../ui'

interface Props {
  open: boolean
  middleware: MiddlewareTypeInfo
  /** Lista completa de agentes (já trazida pela tela pai pra evitar dupla request). */
  agents: Agent[]
  /** Ids que JÁ têm esse middleware ativo — não aparecem no picker. */
  alreadyUsing: ReadonlyArray<string>
  onClose: () => void
  onAttached: (agent: Agent) => void
}

/**
 * Monta o payload do PUT preservando o agente atual e adicionando uma entry
 * em middlewares com settings default. Tipos de campo enviados são os que
 * o backend exige (CreateAgentRequest); demais campos viajam intactos.
 */
function buildPayload(agent: Agent, mw: MiddlewareTypeInfo, changeReason: string): UpdateAgentFullBody {
  const existing: AgentMiddleware[] = Array.isArray(agent.middlewares) ? agent.middlewares : []
  // Se por algum motivo o middleware já está no array desativado, religamos
  // (substitui pela versão enabled com settings default).
  const withoutDup = existing.filter((m) => m.type !== mw.name)
  const next: AgentMiddleware = {
    type: mw.name,
    enabled: true,
    settings: defaultSettingsFor(mw),
  }
  return {
    id: agent.id,
    name: agent.name,
    description: agent.description ?? null,
    // Model/provider podem vir parcialmente populados quando o agente usa
    // PredefinedModelId — backend hidrata em runtime. Repassamos o que veio.
    model: agent.model ?? { deploymentName: '' },
    provider: agent.provider ?? undefined,
    authorInstructions: agent.authorInstructions ?? null,
    tools: agent.tools ?? [],
    middlewares: [...withoutDup, next],
    metadata: agent.metadata ?? {},
    changeReason,
    breakingChange: false,
  }
}

export function AttachMiddlewareModal({
  open, middleware, agents, alreadyUsing, onClose, onAttached,
}: Props) {
  const [search, setSearch] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const eligible = useMemo(() => {
    const usingSet = new Set(alreadyUsing)
    const q = search.trim().toLowerCase()
    return agents
      .filter((a) => a.enabled !== false && !usingSet.has(a.id))
      .filter((a) => !q || a.id.toLowerCase().includes(q) || a.name.toLowerCase().includes(q))
      .sort((a, b) => a.name.localeCompare(b.name))
  }, [agents, alreadyUsing, search])

  const handlePick = async (agent: Agent) => {
    setError(null)
    setBusyId(agent.id)
    try {
      const payload = buildPayload(
        agent,
        middleware,
        `Middleware ${middleware.name} anexado via tela admin`,
      )
      const updated = await updateAgentFull(agent.id, payload)
      onAttached(updated)
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível anexar o middleware. Tente novamente.'))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={`Anexar ${middleware.label || middleware.name}`}
      description={
        <>
          Selecione um agente publicado. Settings serão aplicados com os defaults do catálogo.
          O agente não retorna pra aprovação — atualização direta como admin.
        </>
      }
      footer={
        <div className="flex justify-end">
          <Button variant="ghost" onClick={onClose}>Cancelar</Button>
        </div>
      }
    >
      <div className="space-y-3">
        <Input
          placeholder="Buscar por id ou nome…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          leftAddon={<SearchIcon className="h-4 w-4" />}
        />
        {error && <ErrorMessage message={error} />}

        <div className="max-h-96 space-y-1.5 overflow-y-auto pr-1">
          {eligible.length === 0 ? (
            <p className="rounded-md border border-dashed border-border bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">
              Nenhum agente publicado disponível pra esse middleware.
            </p>
          ) : (
            eligible.map((agent) => {
              const busy = busyId === agent.id
              return (
                <button
                  key={agent.id}
                  type="button"
                  onClick={() => !busyId && handlePick(agent)}
                  disabled={!!busyId}
                  className={cn(
                    'flex w-full items-center justify-between gap-3 rounded-md border border-border bg-surface px-3 py-2 text-left transition',
                    busy ? 'opacity-60' : 'hover:border-accent/40 hover:bg-surface-hover',
                    busyId && !busy && 'opacity-40 cursor-not-allowed',
                  )}
                >
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="truncate text-sm font-medium">{agent.name}</span>
                      {agent.type && (
                        <Badge tone="neutral" className="shrink-0">{agent.type}</Badge>
                      )}
                      {agent.visibility === 'global' && (
                        <Badge tone="accent" className="shrink-0">global</Badge>
                      )}
                    </div>
                    <code className="block truncate text-[11px] text-fg-dim">{agent.id}</code>
                  </div>
                  {busy && <Spinner className="h-4 w-4 shrink-0 text-fg-muted" />}
                </button>
              )
            })
          )}
        </div>
      </div>
    </Modal>
  )
}
