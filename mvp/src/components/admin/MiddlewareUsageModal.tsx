import { useState } from 'react'
import {
  updateAgentFull,
  type Agent,
  type AgentMiddleware,
  type UpdateAgentFullBody,
} from '../../api/agents'
import { friendlyError } from '../../api/client'
import { type MiddlewareTypeInfo } from '../../api/middlewares'
import {
  Badge,
  Button,
  ErrorMessage,
  Modal,
  Spinner,
  cn,
} from '../../ui'

interface Props {
  open: boolean
  middleware: MiddlewareTypeInfo
  /** Agentes que TÊM o middleware ativo (filtrado pela tela pai). */
  agents: Agent[]
  onClose: () => void
  onRemoved: (agentId: string) => void
}

function buildRemovalPayload(agent: Agent, middlewareName: string, changeReason: string): UpdateAgentFullBody {
  const existing: AgentMiddleware[] = Array.isArray(agent.middlewares) ? agent.middlewares : []
  const next = existing.filter((m) => m.type !== middlewareName)
  return {
    id: agent.id,
    name: agent.name,
    description: agent.description ?? null,
    model: agent.model ?? { deploymentName: '' },
    provider: agent.provider ?? undefined,
    instructions: agent.instructions ?? null,
    tools: agent.tools ?? [],
    middlewares: next,
    metadata: agent.metadata ?? {},
    changeReason,
    breakingChange: false,
  }
}

export function MiddlewareUsageModal({ open, middleware, agents, onClose, onRemoved }: Props) {
  const [busyId, setBusyId] = useState<string | null>(null)
  const [confirmId, setConfirmId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleRemove = async (agent: Agent) => {
    setError(null)
    setBusyId(agent.id)
    try {
      const payload = buildRemovalPayload(
        agent,
        middleware.name,
        `Middleware ${middleware.name} removido via tela admin`,
      )
      await updateAgentFull(agent.id, payload)
      onRemoved(agent.id)
      setConfirmId(null)
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível remover o middleware. Tente novamente.'))
    } finally {
      setBusyId(null)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={`Agentes usando ${middleware.label || middleware.name}`}
      description="Remover um middleware faz update direto no agente publicado, sem aprovação."
      footer={
        <div className="flex justify-end">
          <Button variant="ghost" onClick={onClose}>Fechar</Button>
        </div>
      }
    >
      <div className="space-y-3">
        {error && <ErrorMessage message={error} />}

        {agents.length === 0 ? (
          <p className="rounded-md border border-dashed border-border bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">
            Nenhum agente está usando esse middleware no momento.
          </p>
        ) : (
          <div className="max-h-96 space-y-1.5 overflow-y-auto pr-1">
            {agents.map((agent) => {
              const busy = busyId === agent.id
              const confirming = confirmId === agent.id
              return (
                <div
                  key={agent.id}
                  className={cn(
                    'flex items-center justify-between gap-3 rounded-md border border-border bg-surface px-3 py-2',
                    busy && 'opacity-60',
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
                  <div className="flex items-center gap-2">
                    {busy && <Spinner className="h-4 w-4 text-fg-muted" />}
                    {confirming ? (
                      <>
                        <Button
                          size="sm"
                          variant="danger"
                          onClick={() => handleRemove(agent)}
                          disabled={!!busyId}
                        >
                          Confirmar remoção
                        </Button>
                        <Button
                          size="sm"
                          variant="ghost"
                          onClick={() => setConfirmId(null)}
                          disabled={!!busyId}
                        >
                          Cancelar
                        </Button>
                      </>
                    ) : (
                      <Button
                        size="sm"
                        variant="ghost"
                        onClick={() => setConfirmId(agent.id)}
                        disabled={!!busyId}
                      >
                        Remover
                      </Button>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </Modal>
  )
}
