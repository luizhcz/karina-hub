import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { friendlyError } from '../api/client'
import { listRouterIntents, type RouterIntent } from '../api/routerIntents'
import {
  createRouterQuickAction,
  deleteRouterQuickAction,
  listRouterQuickActions,
  updateRouterQuickAction,
  type QuickActionBody,
  type RouterQuickAction,
} from '../api/routerQuickActions'
import {
  ArrowLeftIcon,
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Input,
  Modal,
  Select,
  Spinner,
  Textarea,
} from '../ui'

/**
 * CRUD de Quick Actions de um Router. Cada atalho mapeia um Pattern de texto
 * pra uma Intent — quando o usuário envia mensagem que bate no Pattern, o
 * Router retorna a intent direto, sem chamar o LLM.
 *
 * <p>Escopo: o Router precisa ser do tipo `Router` e estar acessível no projeto
 * corrente. O pool de Intents disponíveis vem de `agent_router_intents`.</p>
 */
export function RouterQuickActionsManager() {
  const { id: routerId } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [agent, setAgent] = useState<Agent | null>(null)
  const [intents, setIntents] = useState<RouterIntent[]>([])
  const [actions, setActions] = useState<RouterQuickAction[]>([])
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [editing, setEditing] = useState<RouterQuickAction | null>(null)
  const [creating, setCreating] = useState(false)

  // Intents do pool deste Router (filtra o catálogo global pelos IDs declarados em agent.routerIntentIds).
  const routerIntents = useMemo(() => {
    if (!agent || !agent.routerIntentIds) return [] as RouterIntent[]
    const allowed = new Set(agent.routerIntentIds)
    return intents.filter((i) => allowed.has(i.id))
  }, [agent, intents])

  // Lookup id → name pra renderizar a coluna Intent na tabela com nome humano.
  const intentNameById = useMemo(() => {
    const map = new Map<string, string>()
    for (const i of intents) map.set(i.id, i.name)
    return map
  }, [intents])

  useEffect(() => {
    if (!routerId) return
    let cancelled = false
    setLoading(true)
    setLoadError(null)
    Promise.all([getAgent(routerId), listRouterIntents(), listRouterQuickActions(routerId)])
      .then(([a, allIntents, qa]) => {
        if (cancelled) return
        setAgent(a)
        setIntents(allIntents)
        setActions(qa)
      })
      .catch((err) => {
        if (cancelled) return
        setLoadError(friendlyError(err, 'Falha ao carregar dados do Router.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [routerId])

  const reload = async () => {
    if (!routerId) return
    const qa = await listRouterQuickActions(routerId)
    setActions(qa)
  }

  async function handleDelete(action: RouterQuickAction) {
    if (!routerId) return
    if (!window.confirm(`Remover atalho "${action.displayText}" (${action.pattern})?`)) return
    try {
      await deleteRouterQuickAction(routerId, action.id)
      await reload()
    } catch (err) {
      window.alert(friendlyError(err, 'Falha ao remover atalho.'))
    }
  }

  if (loading) {
    return (
      <div className="flex items-center justify-center p-12">
        <Spinner />
      </div>
    )
  }

  if (loadError || !agent) {
    return <ErrorMessage message={loadError ?? 'Agente não encontrado.'} />
  }

  if (agent.type !== 'Router') {
    return (
      <ErrorMessage
        message={`Agente '${agent.id}' é do tipo ${agent.type} — Quick Actions só fazem sentido pra Router.`}
      />
    )
  }

  const intentsAvailable = routerIntents.length > 0

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4 p-6">
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate(`/agentes/${routerId}`)}
          leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
        >
          Voltar ao agente
        </Button>
      </div>

      <Card>
        <CardHeader
          title="Atalhos (Quick Actions)"
          description={
            <span>
              Router <span className="font-mono">{agent.id}</span> · {agent.name}
            </span>
          }
          actions={
            <Button
              onClick={() => setCreating(true)}
              disabled={!intentsAvailable}
              title={
                intentsAvailable
                  ? undefined
                  : 'Cadastre intents no pool deste Router antes (em /intencoes).'
              }
            >
              Novo atalho
            </Button>
          }
        />
        <div className="px-4 pb-4 text-xs text-fg-muted">
          Atalhos batem em mensagens do usuário antes do LLM ser chamado. Quando uma
          mensagem casa com o <strong>Pattern</strong>, o Router retorna a{' '}
          <strong>Intent</strong> diretamente — zero custo de token. Use <code className="rounded bg-bg-soft px-1">{'*'}</code>
          no final pra capturar texto livre depois (ex.: <code className="rounded bg-bg-soft px-1">{'comprar *'}</code>{' '}
          bate em "comprar PETR4", "comprar VALE3" etc).
        </div>

        {!intentsAvailable && (
          <div className="mx-4 mb-4 rounded border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-700">
            Este Router não tem intents no pool ainda. Adicione intents em{' '}
            <a className="underline" href="/intencoes">
              /intencoes
            </a>{' '}
            e vincule-as ao Router antes de criar atalhos.
          </div>
        )}

        {actions.length === 0 ? (
          <div className="px-4 pb-6 text-sm text-fg-muted">Nenhum atalho cadastrado.</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full border-t border-border text-sm">
              <thead className="bg-bg-soft/50 text-xs uppercase tracking-wider text-fg-muted">
                <tr>
                  <th className="px-4 py-2 text-left">Botão</th>
                  <th className="px-4 py-2 text-left">Pattern</th>
                  <th className="px-4 py-2 text-left">Intent</th>
                  <th className="px-4 py-2 text-left">Descrição</th>
                  <th className="px-4 py-2 text-right">Ações</th>
                </tr>
              </thead>
              <tbody>
                {actions.map((a) => (
                  <tr key={a.id} className="border-t border-border">
                    <td className="px-4 py-2 font-medium">{a.displayText}</td>
                    <td className="px-4 py-2 font-mono text-xs">
                      {a.pattern}
                      {a.hasWildcard && (
                        <Badge tone="accent" className="ml-2">
                          wildcard
                        </Badge>
                      )}
                    </td>
                    <td className="px-4 py-2 font-mono text-xs">{intentNameById.get(a.intent) ?? a.intent}</td>
                    <td className="px-4 py-2 text-xs text-fg-muted">{a.description ?? '—'}</td>
                    <td className="px-4 py-2 text-right">
                      <div className="flex justify-end gap-2">
                        <Button variant="ghost" size="sm" onClick={() => setEditing(a)}>
                          Editar
                        </Button>
                        <Button variant="ghost" size="sm" onClick={() => handleDelete(a)}>
                          Remover
                        </Button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {(creating || editing) && (
        <QuickActionEditorModal
          routerId={routerId!}
          existing={editing}
          intents={routerIntents}
          onClose={() => {
            setCreating(false)
            setEditing(null)
          }}
          onSaved={async () => {
            setCreating(false)
            setEditing(null)
            await reload()
          }}
        />
      )}
    </div>
  )
}

interface EditorModalProps {
  routerId: string
  existing: RouterQuickAction | null
  intents: RouterIntent[]
  onClose: () => void
  onSaved: () => Promise<void> | void
}

function QuickActionEditorModal({ routerId, existing, intents, onClose, onSaved }: EditorModalProps) {
  const [pattern, setPattern] = useState(existing?.pattern ?? '')
  const [displayText, setDisplayText] = useState(existing?.displayText ?? '')
  const [intent, setIntent] = useState(existing?.intent ?? (intents[0]?.id ?? ''))
  const [description, setDescription] = useState(existing?.description ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const normalizedPreview = useMemo(() => normalizePreview(pattern), [pattern])
  const hasWildcard = normalizedPreview.endsWith(' *')
  const isValid =
    pattern.trim().length > 0 && displayText.trim().length > 0 && intent.length > 0

  async function handleSubmit() {
    if (!isValid) return
    setSaving(true)
    setError(null)
    const body: QuickActionBody = {
      pattern,
      displayText: displayText.trim(),
      intent,
      description: description.trim() || null,
    }
    try {
      if (existing) {
        await updateRouterQuickAction(routerId, existing.id, body)
      } else {
        await createRouterQuickAction(routerId, body)
      }
      await onSaved()
    } catch (err) {
      setError(friendlyError(err, 'Falha ao salvar atalho.'))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open onClose={onClose} title={existing ? 'Editar atalho' : 'Novo atalho'}>
      <div className="flex flex-col gap-3">
        <label className="flex flex-col gap-1 text-xs">
          <span className="font-medium">
            Pattern <span className="text-rose-500">*</span>
          </span>
          <Input
            value={pattern}
            onChange={(e) => setPattern(e.target.value)}
            placeholder="comprar *"
            spellCheck={false}
          />
          <span className="text-fg-muted">
            Texto a casar com a mensagem do usuário. Use <code>{'*'}</code> no fim pra capturar
            texto livre (ex.: <code>{'comprar *'}</code> bate em "comprar PETR4").
          </span>
          <span className="text-fg-muted">
            Normalizado:{' '}
            <code className="rounded bg-bg-soft px-1 font-mono">{normalizedPreview || '—'}</code>
          </span>
        </label>

        <label className="flex flex-col gap-1 text-xs">
          <span className="font-medium">
            Texto do botão <span className="text-rose-500">*</span>
          </span>
          <Input
            value={displayText}
            onChange={(e) => setDisplayText(e.target.value)}
            placeholder="Comprar"
          />
          <span className="text-fg-muted">
            {hasWildcard
              ? 'Botão pré-popula o input com este texto + espaço; usuário completa antes de enviar.'
              : 'Botão envia este texto imediatamente ao clicar.'}
          </span>
        </label>

        <label className="flex flex-col gap-1 text-xs">
          <span className="font-medium">
            Intent <span className="text-rose-500">*</span>
          </span>
          <Select
            value={intent}
            onChange={(e) => setIntent(e.target.value)}
            options={intents.map((i) => ({ value: i.id, label: i.name }))}
            placeholder="Selecione uma intent…"
          />
          <span className="text-fg-muted">Intent retornada quando o pattern bate.</span>
        </label>

        <label className="flex flex-col gap-1 text-xs">
          <span className="font-medium">Descrição (opcional)</span>
          <Textarea
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            rows={2}
            placeholder="Tooltip do botão"
          />
        </label>

        {error && <div className="rounded bg-rose-500/10 px-3 py-2 text-xs text-rose-700">{error}</div>}

        <div className="mt-2 flex justify-end gap-2">
          <Button variant="ghost" onClick={onClose} disabled={saving}>
            Cancelar
          </Button>
          <Button onClick={handleSubmit} loading={saving} disabled={!isValid || saving}>
            {existing ? 'Salvar' : 'Criar'}
          </Button>
        </div>
      </div>
    </Modal>
  )
}

// Espelha QuickActionPatternMatcher.Normalize do backend pra preview live no editor.
// Match exato com a função C# evita surpresa entre client/server.
function normalizePreview(input: string): string {
  if (!input) return ''
  const lower = input.trim().toLowerCase()
  return lower.split(/\s+/).filter(Boolean).join(' ')
}
