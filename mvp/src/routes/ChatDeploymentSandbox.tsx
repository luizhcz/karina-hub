import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { friendlyError } from '../api/client'
import {
  getWorkflow,
  isChatDeployment,
  listWorkflowVersions,
  type Workflow,
  type WorkflowVersion,
} from '../api/workflows'
import { getOperationalMemory, type OperationalMemory } from '../api/operationalMemory'
import { listAgents, type Agent, type AgentType } from '../api/agents'
import {
  useChatStream,
  type ChatBubble,
  type ChatRawEvent,
  type ChatToolCall,
} from '../hooks/useChatStream'
import {
  ArrowLeftIcon,
  ArrowRightIcon,
  Button,
  Card,
  ErrorMessage,
  Select,
  Spinner,
  cn,
} from '../ui'

const HITL_TOOL_NAME = 'request_approval'
const VERSION_CURRENT = ''

type SidePanelTab = 'events' | 'memory'

interface AgentMemoryState {
  status: 'idle' | 'loading' | 'loaded' | 'empty' | 'error'
  data: OperationalMemory | null
  error: string | null
}

export function ChatDeploymentSandbox() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [workflow, setWorkflow] = useState<Workflow | null>(null)
  const [versions, setVersions] = useState<WorkflowVersion[]>([])
  const [selectedVersionId, setSelectedVersionId] = useState<string>(VERSION_CURRENT)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [draft, setDraft] = useState('')
  const [sidePanelOpen, setSidePanelOpen] = useState(false)
  const [activeTab, setActiveTab] = useState<SidePanelTab>('events')
  // Backend AG-UI emite STEP_STARTED só com stepId/stepName — sem agentType.
  // Carregamos a lista de agents 1x e indexamos por id pra rotular cada step
  // com o tipo (Router, Conversational, Worker, ToolRunner, Custom).
  const [agentTypeById, setAgentTypeById] = useState<Map<string, AgentType>>(new Map())
  // Subset dos agents que têm operational memory configurada — evita chamar
  // /operational-memory pra Router/agents sem schema (que retornam 404 e
  // poluem o console).
  const [agentsWithMemoryRef, setAgentsWithMemoryRef] = useState<Set<string>>(new Set())

  const stream = useChatStream({
    workflowId: id ?? '',
    workflowVersionId: selectedVersionId || null,
  })

  // Mapa agentId → estado da memória operacional. Populado on-demand quando
  // um STEP_STARTED emite stepId novo. ThreadId vem do RUN_STARTED.
  const [memories, setMemories] = useState<Map<string, AgentMemoryState>>(new Map())
  const fetchedKeysRef = useRef(new Set<string>())

  useEffect(() => {
    if (!id) return
    let cancelled = false
    getWorkflow(id)
      .then((w) => {
        if (cancelled) return
        if (!isChatDeployment(w)) {
          setLoadError('Esta implantação não é do tipo Chat.')
        }
        setWorkflow(w)
      })
      .catch((err) => {
        if (cancelled) return
        setLoadError(friendlyError(err, 'Não foi possível carregar a implantação.'))
      })
    listWorkflowVersions(id)
      .then((list) => {
        if (!cancelled) setVersions(list)
      })
      .catch(() => undefined)
    // Carrega tipos dos agents do projeto pra rotular cada step do stream.
    // Aproveita pra indexar o subset que tem operational memory configurada
    // — fetch posterior só dispara pros agents desse subset.
    listAgents()
      .then((agents: Agent[]) => {
        if (cancelled) return
        const map = new Map<string, AgentType>()
        const withMemory = new Set<string>()
        for (const a of agents) {
          if (a.type) map.set(a.id, a.type)
          if (a.operationalMemory?.schema) withMemory.add(a.id)
        }
        setAgentTypeById(map)
        setAgentsWithMemoryRef(withMemory)
      })
      .catch(() => undefined)
    return () => {
      cancelled = true
    }
  }, [id])

  // Quando um step novo aparece + threadId está populado, busca a memória
  // operacional desse agente pra esse escopo. Idempotente via fetchedKeysRef
  // (chave = agentId+threadId) — recarrega só se o user clicar em "atualizar".
  // Agents sem memória configurada (Router classifier, Conversational sem
  // schema declarado) são puláveis — o endpoint retorna 404 e o browser
  // loga ruído no DevTools sem nenhum ganho funcional.
  useEffect(() => {
    const tid = stream.threadId
    if (!tid) return
    for (const step of stream.steps) {
      const agentId = step.id
      const key = `${agentId}::${tid}`
      if (fetchedKeysRef.current.has(key)) continue
      fetchedKeysRef.current.add(key)
      if (!agentsWithMemoryRef.has(agentId)) {
        setMemories((prev) => {
          const next = new Map(prev)
          next.set(agentId, { status: 'empty', data: null, error: null })
          return next
        })
        continue
      }
      setMemories((prev) => {
        const next = new Map(prev)
        next.set(agentId, { status: 'loading', data: null, error: null })
        return next
      })
      void getOperationalMemory(agentId, tid, 'conversation')
        .then((mem) => {
          setMemories((prev) => {
            const next = new Map(prev)
            next.set(agentId, {
              status: mem ? 'loaded' : 'empty',
              data: mem,
              error: null,
            })
            return next
          })
        })
        .catch((err: unknown) => {
          setMemories((prev) => {
            const next = new Map(prev)
            next.set(agentId, {
              status: 'error',
              data: null,
              error: friendlyError(err, 'Falha ao buscar memória.'),
            })
            return next
          })
        })
    }
  }, [stream.steps, stream.threadId, agentsWithMemoryRef])

  // Reset limpa também o cache de memórias pra próxima conversa começar limpa.
  function handleReset() {
    stream.reset()
    fetchedKeysRef.current.clear()
    setMemories(new Map())
  }

  const isStreaming = stream.status === 'streaming'
  const canSend = !isStreaming && draft.trim().length > 0

  function handleSend() {
    if (!canSend) return
    const text = draft
    setDraft('')
    void stream.send(text)
  }

  function handleKeyDown(e: React.KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  if (!id) {
    return <ErrorMessage message="ID da implantação ausente." />
  }

  const mergedAgentTypes = useMemo(
    () => mergeAgentTypes(agentTypeById, stream.agentTypeByNodeId),
    [agentTypeById, stream.agentTypeByNodeId],
  )

  const containerMaxWidth = sidePanelOpen ? 'max-w-7xl' : 'max-w-4xl'

  return (
    <div className={cn('mx-auto flex h-[calc(100vh-4rem)] flex-col gap-4', containerMaxWidth)}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Button
            variant="ghost"
            leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
            onClick={() => navigate(`/implantacoes/chat/${id}`)}
          >
            Voltar ao editor
          </Button>
          <div>
            <h1 className="text-xl font-semibold">{workflow?.name ?? 'Sandbox de Chat'}</h1>
            <p className="text-xs text-fg-muted">Ambiente de teste efêmero — histórico se perde no reload.</p>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className="inline-flex items-center rounded-md border border-teal-500/40 bg-teal-500/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider text-teal-600 dark:text-teal-400">
            Chat
          </span>
          {/* Selector de versão. Default "current" preserva back-compat
              (sem header x-version → backend usa estado mutável atual).
              Disable durante stream pra não trocar de versão mid-execução. */}
          <Select
            value={selectedVersionId}
            onChange={(e) => setSelectedVersionId(e.target.value)}
            disabled={isStreaming || versions.length === 0}
            options={[
              { value: VERSION_CURRENT, label: versions.length > 0 ? 'Versão atual (current)' : 'Versão atual' },
              ...versions.map((v) => ({
                value: v.workflowVersionId,
                label: `r${v.revision} · ${v.contentHash.slice(0, 8)}`,
              })),
            ]}
            className="min-w-[180px]"
          />
          <Button
            variant={sidePanelOpen ? 'secondary' : 'ghost'}
            onClick={() => setSidePanelOpen((v) => !v)}
          >
            {sidePanelOpen ? 'Ocultar debug' : 'Debug'}
          </Button>
          {isStreaming && (
            <Button variant="ghost" onClick={() => void stream.cancel()}>
              Cancelar
            </Button>
          )}
          {stream.bubbles.length > 0 && !isStreaming && (
            <Button variant="ghost" onClick={handleReset}>
              Limpar
            </Button>
          )}
        </div>
      </div>

      {loadError && <ErrorMessage message={loadError} />}

      <div className="flex flex-1 min-h-0 gap-4">
        <Card padded={false} className="flex flex-1 min-h-0 flex-col overflow-hidden">
          <BubbleStack
            bubbles={stream.bubbles}
            toolCalls={stream.toolCalls}
            isStreaming={isStreaming}
            sharedState={stream.sharedState}
            agentTypeById={mergedAgentTypes}
            onResolveHitl={(toolCallId, response) => void stream.resolveHitl(toolCallId, response)}
          />
          {stream.errorMessage && (
            <div className="border-t border-border bg-bg-soft px-4 py-2 text-sm text-rose-500">
              {stream.errorMessage}
            </div>
          )}
          <div className="border-t border-border bg-bg-soft/50 px-4 py-3">
            <div className="flex items-end gap-2">
              <textarea
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder="Digite uma mensagem pra testar o chat…"
                rows={1}
                disabled={isStreaming || !!loadError}
                className={cn(
                  'block flex-1 resize-none rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-5 text-fg placeholder:text-fg-dim',
                  'focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent',
                  'disabled:cursor-not-allowed disabled:opacity-60',
                )}
              />
              <Button
                aria-label="Enviar mensagem"
                onClick={handleSend}
                loading={isStreaming}
                disabled={!canSend || !!loadError}
                rightIcon={!isStreaming ? <ArrowRightIcon className="h-4 w-4" /> : undefined}
                className="shrink-0"
              >
                Enviar
              </Button>
            </div>
            <p className="mt-1.5 text-[11px] text-fg-dim">
              Enter pra enviar · Shift+Enter pra quebrar linha
            </p>
          </div>
        </Card>

        {sidePanelOpen && (
          <SidePanel
            tab={activeTab}
            onTabChange={setActiveTab}
            rawEvents={stream.rawEvents}
            memories={memories}
            steps={stream.steps}
            threadId={stream.threadId}
            agentTypeById={mergeAgentTypes(agentTypeById, stream.agentTypeByNodeId)}
          />
        )}
      </div>
    </div>
  )
}

interface BubbleStackProps {
  bubbles: ChatBubble[]
  toolCalls: ChatToolCall[]
  isStreaming: boolean
  sharedState: unknown
  agentTypeById: Map<string, AgentType>
  onResolveHitl: (toolCallId: string, response: string) => void
}

function BubbleStack({
  bubbles,
  toolCalls,
  isStreaming,
  sharedState,
  agentTypeById,
  onResolveHitl,
}: BubbleStackProps) {
  const scrollRef = useRef<HTMLDivElement | null>(null)

  // Tool calls são exibidos inline depois da bubble assistant que os disparou.
  // Indexamos por parentMessageId pra encaixar entre bubbles.
  const toolCallsByParent = useMemo(() => {
    const map = new Map<string, ChatToolCall[]>()
    for (const tc of toolCalls) {
      const key = tc.parentMessageId ?? '__orphan__'
      const list = map.get(key) ?? []
      list.push(tc)
      map.set(key, list)
    }
    return map
  }, [toolCalls])

  useLayoutEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTop = el.scrollHeight
  }, [bubbles, toolCalls, isStreaming])

  if (bubbles.length === 0 && !isStreaming) {
    return (
      <div className="flex flex-1 flex-col items-center justify-center gap-2 px-6 py-10 text-center text-sm text-fg-muted">
        <p>Comece a conversa enviando uma mensagem.</p>
        <p className="text-xs">Router conversacional → branch Conversational → resposta.</p>
      </div>
    )
  }

  return (
    <div ref={scrollRef} className="flex-1 overflow-y-auto px-4 py-4">
      <div className="flex flex-col gap-3">
        {bubbles.map((b) => {
          const agentType = b.agentId ? agentTypeById.get(b.agentId) : undefined
          // Router: bubble vira card de decisão (intent + confidence + reasoning)
          // em vez de markdown cru — o content é JSON estruturado do schema
          // router_intent, então renderizar como texto é ilegível pro user.
          const RouterContent =
            agentType === 'Router'
              ? tryParseRouterDecision(b.content)
              : null
          return (
            <div key={b.id} className="flex flex-col gap-2">
              {RouterContent ? (
                <RouterDecisionCard
                  agentId={b.agentId!}
                  decision={RouterContent}
                  streaming={isStreaming && !b.complete}
                />
              ) : (
                <BubbleRow
                  bubble={b}
                  agentType={agentType}
                  streaming={isStreaming && !b.complete}
                />
              )}
              {(toolCallsByParent.get(b.id) ?? []).map((tc) => (
                <ToolCallRow key={tc.id} call={tc} onResolveHitl={onResolveHitl} />
              ))}
            </div>
          )
        })}
        {(toolCallsByParent.get('__orphan__') ?? []).map((tc) => (
          <ToolCallRow key={tc.id} call={tc} onResolveHitl={onResolveHitl} />
        ))}
        {sharedState != null && Object.keys(sharedState as object).length > 0 && (
          <details className="rounded-md border border-border bg-bg-soft px-3 py-2 text-xs">
            <summary className="cursor-pointer font-medium">Estado compartilhado</summary>
            <pre className="mt-2 overflow-x-auto">{JSON.stringify(sharedState, null, 2)}</pre>
          </details>
        )}
        {isStreaming && bubbles.every((b) => b.complete) && (
          <div className="flex items-center gap-2 text-xs text-fg-muted">
            <Spinner className="h-3 w-3" /> Processando…
          </div>
        )}
      </div>
    </div>
  )
}

function BubbleRow({
  bubble,
  agentType,
  streaming,
}: {
  bubble: ChatBubble
  agentType?: AgentType
  streaming: boolean
}) {
  const isUser = bubble.role === 'user'
  return (
    <div className={cn('flex flex-col', isUser ? 'items-end' : 'items-start')}>
      {/* Header pequeno: tipo + agentId pra dar contexto de quem respondeu.
          Só pra bubbles assistant — user não precisa de header. */}
      {!isUser && bubble.agentId && agentType && (
        <div className="mb-1 flex items-center gap-1.5 px-1 text-[10px] text-fg-muted">
          <AgentTypeBadge type={agentType} />
          <span className="font-mono">{bubble.agentId}</span>
        </div>
      )}
      <div
        className={cn(
          'max-w-[80%] rounded-2xl px-4 py-2 text-sm leading-relaxed',
          isUser
            ? 'bg-accent text-accent-contrast'
            : 'border border-border bg-bg-soft text-fg',
        )}
      >
        {isUser ? (
          <span className="whitespace-pre-wrap">{bubble.content}</span>
        ) : (
          <div className="prose prose-sm dark:prose-invert max-w-none">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{bubble.content || ' '}</ReactMarkdown>
          </div>
        )}
        {streaming && !isUser && (
          <span className="ml-1 inline-block h-3 w-1 animate-pulse bg-fg-muted align-middle" aria-hidden />
        )}
      </div>
    </div>
  )
}

// Shape produzido pelo Conversational template do Router (schema router_intent).
// `confidence` é número 0..1; o renderer mostra como porcentagem.
interface RouterDecision {
  intent: string
  confidence: number
  reason: string
}

function tryParseRouterDecision(raw: string): RouterDecision | null {
  if (!raw) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object') return null
  const o = parsed as Record<string, unknown>
  if (
    typeof o.intent !== 'string'
    || typeof o.confidence !== 'number'
    || typeof o.reason !== 'string'
  ) {
    return null
  }
  return { intent: o.intent, confidence: o.confidence, reason: o.reason }
}

function RouterDecisionCard({
  agentId,
  decision,
  streaming,
}: {
  agentId: string
  decision: RouterDecision
  streaming: boolean
}) {
  const pct = Math.round(decision.confidence * 100)
  // Tons de confidence: alta (≥0.8) verde, média (0.5–0.79) âmbar, baixa rosa.
  const confidenceTone =
    decision.confidence >= 0.8
      ? 'border-success/40 bg-success/10 text-success'
      : decision.confidence >= 0.5
        ? 'border-amber-500/40 bg-amber-500/10 text-amber-600 dark:text-amber-400'
        : 'border-rose-500/40 bg-rose-500/10 text-rose-600 dark:text-rose-400'

  return (
    <div className="flex flex-col items-start">
      <div className="mb-1 flex items-center gap-1.5 px-1 text-[10px] text-fg-muted">
        <AgentTypeBadge type="Router" />
        <span className="font-mono">{agentId}</span>
      </div>
      <div className="w-full max-w-[80%] rounded-2xl border border-violet-500/30 bg-violet-500/[0.04] p-3 text-sm">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex flex-col">
            <span className="text-[10px] uppercase tracking-wider text-fg-dim">
              Roteado pra intenção
            </span>
            <span className="font-mono text-[13px] font-semibold text-fg">{decision.intent}</span>
          </div>
          <span
            className={cn(
              'inline-flex items-center rounded-md border px-2 py-0.5 text-[11px] font-semibold tabular-nums',
              confidenceTone,
            )}
          >
            {pct}% confiança
          </span>
        </div>
        <div className="mt-2 border-t border-border/60 pt-2">
          <span className="text-[10px] uppercase tracking-wider text-fg-dim">Raciocínio</span>
          <p className="mt-1 text-xs leading-relaxed text-fg-muted">{decision.reason}</p>
        </div>
        {streaming && (
          <div className="mt-2 flex items-center gap-1.5 text-[10px] text-fg-dim">
            <Spinner className="h-3 w-3" /> Encaminhando…
          </div>
        )}
      </div>
    </div>
  )
}

function ToolCallRow({
  call,
  onResolveHitl,
}: {
  call: ChatToolCall
  onResolveHitl: (toolCallId: string, response: string) => void
}) {
  const isHitl = call.name === HITL_TOOL_NAME
  const awaitingApproval = isHitl && call.complete && call.result == null

  return (
    <div className="ml-4 rounded-md border border-dashed border-border bg-bg-soft px-3 py-2 text-xs">
      <div className="flex items-center justify-between gap-2">
        <span className="font-medium">
          {isHitl ? 'Aprovação humana solicitada' : `Ferramenta: ${call.name}`}
        </span>
        <span className="text-fg-muted">
          {call.result != null ? 'concluída' : call.complete ? 'aguardando' : 'em andamento'}
        </span>
      </div>
      {call.args && (
        <details className="mt-1">
          <summary className="cursor-pointer text-fg-muted">Argumentos</summary>
          <pre className="mt-1 overflow-x-auto">{call.args}</pre>
        </details>
      )}
      {call.result != null && (
        <details className="mt-1" open>
          <summary className="cursor-pointer text-fg-muted">Resultado</summary>
          <pre className="mt-1 overflow-x-auto">{call.result}</pre>
        </details>
      )}
      {awaitingApproval && (
        <div className="mt-2 flex gap-2">
          <Button size="sm" onClick={() => onResolveHitl(call.id, 'approved')}>
            Aprovar
          </Button>
          <Button size="sm" variant="ghost" onClick={() => onResolveHitl(call.id, 'rejected')}>
            Rejeitar
          </Button>
        </div>
      )}
    </div>
  )
}

interface SidePanelProps {
  tab: SidePanelTab
  onTabChange: (tab: SidePanelTab) => void
  rawEvents: ChatRawEvent[]
  memories: Map<string, AgentMemoryState>
  steps: { id: string; name: string }[]
  threadId: string | null
  agentTypeById: Map<string, AgentType>
}

function SidePanel({
  tab,
  onTabChange,
  rawEvents,
  memories,
  steps,
  threadId,
  agentTypeById,
}: SidePanelProps) {
  return (
    <Card padded={false} className="flex w-[420px] min-h-0 flex-col overflow-hidden">
      <div className="flex border-b border-border bg-bg-soft/40">
        <TabButton active={tab === 'events'} onClick={() => onTabChange('events')}>
          Eventos SSE
          <span className="ml-1 text-fg-dim">({rawEvents.length})</span>
        </TabButton>
        <TabButton active={tab === 'memory'} onClick={() => onTabChange('memory')}>
          Memória
          <span className="ml-1 text-fg-dim">({memories.size})</span>
        </TabButton>
      </div>
      {tab === 'events' ? (
        <EventsPanel rawEvents={rawEvents} />
      ) : (
        <MemoryPanel
          memories={memories}
          steps={steps}
          threadId={threadId}
          agentTypeById={agentTypeById}
        />
      )}
    </Card>
  )
}

function TabButton({
  active,
  onClick,
  children,
}: {
  active: boolean
  onClick: () => void
  children: React.ReactNode
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex-1 px-3 py-2 text-xs font-medium transition-colors',
        active
          ? 'border-b-2 border-accent text-fg'
          : 'text-fg-muted hover:text-fg',
      )}
    >
      {children}
    </button>
  )
}

function EventsPanel({ rawEvents }: { rawEvents: ChatRawEvent[] }) {
  const scrollRef = useRef<HTMLDivElement | null>(null)

  useLayoutEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTop = el.scrollHeight
  }, [rawEvents.length])

  if (rawEvents.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center px-4 py-10 text-center text-xs text-fg-muted">
        Nenhum evento ainda. Envie uma mensagem pra começar.
      </div>
    )
  }

  return (
    <div ref={scrollRef} className="flex-1 overflow-y-auto px-3 py-3 text-xs">
      <ol className="space-y-1.5">
        {rawEvents.map((evt) => (
          <li key={evt.seq} className="rounded-md border border-border bg-bg-soft/50">
            <details>
              <summary className="cursor-pointer px-2.5 py-1.5 font-mono text-[11px]">
                <span className="text-fg-dim">#{evt.seq}</span>
                <span className="ml-2 font-semibold text-fg">{evt.type}</span>
                {evt.serverId && <span className="ml-2 text-fg-dim">id={evt.serverId}</span>}
              </summary>
              <pre className="max-h-60 overflow-auto border-t border-border px-2.5 py-1.5 font-mono text-[10px] leading-snug text-fg-muted">
                {JSON.stringify(evt.payload, null, 2)}
              </pre>
            </details>
          </li>
        ))}
      </ol>
    </div>
  )
}

function MemoryPanel({
  memories,
  steps,
  threadId,
  agentTypeById,
}: {
  memories: Map<string, AgentMemoryState>
  steps: { id: string; name: string }[]
  threadId: string | null
  agentTypeById: Map<string, AgentType>
}) {
  // Deriva ordem dos agentes pela ordem dos steps recebidos (não pela ordem
  // alfabética do Map). Cada agentId só aparece uma vez.
  const orderedAgentIds = useMemo(() => {
    const seen = new Set<string>()
    const result: string[] = []
    for (const s of steps) {
      if (!seen.has(s.id)) {
        seen.add(s.id)
        result.push(s.id)
      }
    }
    return result
  }, [steps])

  if (!threadId) {
    return (
      <div className="flex flex-1 items-center justify-center px-4 py-10 text-center text-xs text-fg-muted">
        Memória disponível após o RUN_STARTED (precisa do threadId).
      </div>
    )
  }

  if (orderedAgentIds.length === 0) {
    return (
      <div className="flex flex-1 items-center justify-center px-4 py-10 text-center text-xs text-fg-muted">
        Nenhum agente executado ainda nesta thread.
      </div>
    )
  }

  return (
    <div className="flex-1 overflow-y-auto px-3 py-3 text-xs">
      <p className="mb-3 text-[10px] text-fg-dim">
        thread: <span className="font-mono">{threadId.slice(0, 12)}…</span>
      </p>
      <ol className="space-y-2">
        {orderedAgentIds.map((agentId) => {
          const mem = memories.get(agentId) ?? { status: 'idle', data: null, error: null }
          const agentType = agentTypeById.get(agentId)
          return (
            <li key={agentId} className="rounded-md border border-border bg-bg-soft/50 p-2.5">
              <div className="flex items-center justify-between gap-2">
                <div className="flex min-w-0 items-center gap-1.5">
                  <span className="truncate font-mono text-[11px] font-semibold">{agentId}</span>
                  {agentType && <AgentTypeBadge type={agentType} />}
                </div>
                <MemoryStatusBadge status={mem.status} />
              </div>
              {mem.status === 'loaded' && mem.data && (
                <details className="mt-1.5" open>
                  <summary className="cursor-pointer text-[11px] text-fg-muted">
                    payload (v{mem.data.version})
                  </summary>
                  <pre className="mt-1 max-h-60 overflow-auto font-mono text-[10px] leading-snug">
                    {JSON.stringify(mem.data.payload, null, 2)}
                  </pre>
                </details>
              )}
              {mem.status === 'empty' && (
                <p className="mt-1 text-[11px] text-fg-muted">Nenhuma memória registrada ainda.</p>
              )}
              {mem.status === 'error' && mem.error && (
                <p className="mt-1 text-[11px] text-rose-500">{mem.error}</p>
              )}
            </li>
          )
        })}
      </ol>
    </div>
  )
}

function MemoryStatusBadge({ status }: { status: AgentMemoryState['status'] }) {
  switch (status) {
    case 'loading':
      return <Spinner className="h-3 w-3 text-fg-muted" />
    case 'loaded':
      return <span className="text-[10px] uppercase tracking-wider text-success">ok</span>
    case 'empty':
      return <span className="text-[10px] uppercase tracking-wider text-fg-dim">vazia</span>
    case 'error':
      return <span className="text-[10px] uppercase tracking-wider text-rose-500">erro</span>
    default:
      return null
  }
}

// Mapeia AgentType pra cor coerente com a paleta usada em outros lugares
// (AgentsList, badges de tipo). Cores soft pra não competir com o status badge.
// Merge: dado vindo do servidor (CUSTOM[agent.lifecycle]) tem precedência sobre
// o cache do listAgents() — backend conhece a Type autoritativa do snapshot
// versionado executado, enquanto listAgents só retorna o tipo atual da
// AgentDefinition (pode estar em flight pra outra rev).
function mergeAgentTypes(
  fromList: Map<string, AgentType>,
  fromStream: Map<string, string>,
): Map<string, AgentType> {
  const merged = new Map(fromList)
  for (const [nodeId, type] of fromStream) {
    if (isAgentType(type)) merged.set(nodeId, type)
  }
  return merged
}

function isAgentType(value: string): value is AgentType {
  return (
    value === 'Router'
    || value === 'Conversational'
    || value === 'Worker'
    || value === 'ToolRunner'
    || value === 'Custom'
  )
}

const AGENT_TYPE_PALETTE: Record<AgentType, string> = {
  Router: 'border-violet-500/40 bg-violet-500/10 text-violet-600 dark:text-violet-400',
  Conversational: 'border-teal-500/40 bg-teal-500/10 text-teal-600 dark:text-teal-400',
  Worker: 'border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-400',
  ToolRunner: 'border-sky-500/40 bg-sky-500/10 text-sky-600 dark:text-sky-400',
  Custom: 'border-border bg-bg-soft text-fg-muted',
}

function AgentTypeBadge({ type }: { type: AgentType }) {
  return (
    <span
      className={cn(
        'inline-flex shrink-0 items-center rounded-md border px-1.5 py-px text-[9px] font-semibold uppercase tracking-wider',
        AGENT_TYPE_PALETTE[type] ?? AGENT_TYPE_PALETTE.Custom,
      )}
    >
      {type}
    </span>
  )
}
