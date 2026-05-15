import { useCallback, useEffect, useMemo, useState } from 'react'
import { friendlyError } from '../../api/client'
import {
  getMiddlewareCatalog,
  type MiddlewareTypeInfo,
} from '../../api/middlewares'
import { listAgents, type Agent } from '../../api/agents'
import { useIsAdmin } from '../../stores/me'
import { AttachMiddlewareModal } from '../../components/admin/AttachMiddlewareModal'
import { MiddlewareUsageModal } from '../../components/admin/MiddlewareUsageModal'
import {
  Badge,
  Button,
  Card,
  EmptyState,
  ErrorMessage,
  PlugIcon,
  Spinner,
  cn,
} from '../../ui'

// Conta agentes que têm o middleware ativo (enabled !== false e type bate).
// Decisão de produto: agentes com `enabled === false` no agent_definitions
// (kill-switch) NÃO são considerados — admin que quiser inspeção precisa
// religar primeiro. Mantém a listagem alinhada à intuição "quem está vivo".
function agentsUsing(middlewareName: string, agents: Agent[]): Agent[] {
  return agents.filter(
    (a) =>
      a.enabled !== false
      && Array.isArray(a.middlewares)
      && a.middlewares.some((m) => m.type === middlewareName && m.enabled),
  )
}

function phaseTone(phase: string): 'success' | 'accent' | 'warning' | 'neutral' {
  switch (phase) {
    case 'Pre': return 'accent'
    case 'Post': return 'success'
    case 'Both': return 'warning'
    default: return 'neutral'
  }
}

export function MiddlewaresList() {
  const isAdmin = useIsAdmin()
  const [catalog, setCatalog] = useState<MiddlewareTypeInfo[]>([])
  const [agents, setAgents] = useState<Agent[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [attachTarget, setAttachTarget] = useState<MiddlewareTypeInfo | null>(null)
  const [usageTarget, setUsageTarget] = useState<MiddlewareTypeInfo | null>(null)

  const reload = useCallback(() => {
    let cancelled = false
    setLoading(true)
    setError(null)
    Promise.all([getMiddlewareCatalog(), listAgents()])
      .then(([cat, list]) => {
        if (cancelled) return
        setCatalog(cat)
        setAgents(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar a lista de middlewares.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (isAdmin !== true) return
    return reload()
  }, [isAdmin, reload])

  const usageByType = useMemo(() => {
    const map = new Map<string, Agent[]>()
    for (const mw of catalog) {
      map.set(mw.name, agentsUsing(mw.name, agents))
    }
    return map
  }, [catalog, agents])

  if (isAdmin === null) {
    return (
      <Card className="flex justify-center py-10">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (isAdmin !== true) {
    return (
      <Card>
        <EmptyState
          title="Acesso restrito"
          description="Esta área é exclusiva para administradores."
        />
      </Card>
    )
  }

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Middlewares</h1>
        <p className="mt-1 text-sm text-fg-muted">
          Catálogo de middlewares disponíveis e quais agentes já os utilizam. Anexar/remover
          atualiza agentes publicados diretamente — não passa pelo fluxo de aprovação.
        </p>
      </div>

      {error && <ErrorMessage message={error} />}

      {loading ? (
        <Card className="flex justify-center py-10">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      ) : catalog.length === 0 ? (
        <Card>
          <EmptyState
            title="Nenhum middleware disponível"
            description="O backend não retornou middlewares no catálogo."
          />
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
          {catalog.map((mw) => {
            const using = usageByType.get(mw.name) ?? []
            return (
              <Card key={mw.name} className="flex flex-col gap-3">
                <div className="flex items-start gap-3">
                  <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-accent-subtle text-accent">
                    <PlugIcon className="h-5 w-5" />
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center gap-2">
                      <h3 className="truncate text-base font-semibold">{mw.label || mw.name}</h3>
                      <Badge tone={phaseTone(mw.phase)} className="shrink-0">
                        {mw.phase}
                      </Badge>
                    </div>
                    <code className="block truncate text-[11px] text-fg-dim">{mw.name}</code>
                  </div>
                </div>

                <p className="line-clamp-3 text-sm text-fg-muted">{mw.description}</p>

                {mw.settings.length > 0 && (
                  <div className="rounded-md border border-border bg-bg-soft px-3 py-2">
                    <p className="text-[11px] uppercase tracking-wider text-fg-dim">Settings (defaults)</p>
                    <ul className="mt-1 space-y-0.5 text-xs">
                      {mw.settings.map((s) => (
                        <li key={s.key} className="flex items-center justify-between gap-2">
                          <span className="font-mono text-fg-muted">{s.key}</span>
                          <span className="truncate font-mono text-fg">
                            {s.defaultValue ?? '—'}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </div>
                )}

                <div className="mt-auto flex items-center justify-between border-t border-border pt-3">
                  <button
                    type="button"
                    onClick={() => setUsageTarget(mw)}
                    className={cn(
                      'text-xs font-medium underline-offset-2 transition',
                      using.length > 0
                        ? 'text-accent hover:underline'
                        : 'cursor-default text-fg-dim',
                    )}
                    disabled={using.length === 0}
                  >
                    {using.length === 0
                      ? 'Nenhum agente usando'
                      : `${using.length} agente${using.length === 1 ? '' : 's'} usando`}
                  </button>
                  <Button size="sm" onClick={() => setAttachTarget(mw)}>
                    Anexar em agente
                  </Button>
                </div>
              </Card>
            )
          })}
        </div>
      )}

      {attachTarget && (
        <AttachMiddlewareModal
          open
          middleware={attachTarget}
          agents={agents}
          alreadyUsing={(usageByType.get(attachTarget.name) ?? []).map((a) => a.id)}
          onClose={() => setAttachTarget(null)}
          onAttached={() => {
            setAttachTarget(null)
            reload()
          }}
        />
      )}

      {usageTarget && (
        <MiddlewareUsageModal
          open
          middleware={usageTarget}
          agents={usageByType.get(usageTarget.name) ?? []}
          onClose={() => setUsageTarget(null)}
          onRemoved={() => {
            // Mantém modal aberta — lista interna refaz via reload.
            reload()
          }}
        />
      )}
    </div>
  )
}
