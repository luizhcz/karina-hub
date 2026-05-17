import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { createSession, streamRun, type AgentSession, type StreamEvent } from '../api/agentSessions'
import { ApiError, friendlyError } from '../api/client'
import {
  deleteOperationalMemory,
  getOperationalMemory,
  type OperationalMemory,
} from '../api/operationalMemory'
import {
  AgentIcon,
  ArrowLeftIcon,
  ArrowRightIcon,
  Button,
  Card,
  CloseIcon,
  ErrorMessage,
  IconButton,
  PlusIcon,
  Spinner,
  cn,
} from '../ui'
import { extractConversationalDisplay } from '../utils/conversationalDisplay'
import { OutputDetails, TypingDots, UiComponentChip } from '../components/ConversationalExtras'

interface UserMsg {
  kind: 'user'
  id: string
  text: string
}

interface AssistantMsg {
  kind: 'assistant'
  id: string
  content: string
  toolCalls: ToolCall[]
  streaming: boolean
  errored?: boolean
}

interface ToolCall {
  id: string
  name: string
  result?: string
  error?: string
}

type ChatMsg = UserMsg | AssistantMsg

function shortId() {
  return Math.random().toString(36).slice(2, 10)
}

export function AgentSandbox() {
  const navigate = useNavigate()
  const { id } = useParams<{ id: string }>()

  const [agent, setAgent] = useState<Agent | null>(null)
  const [agentLoading, setAgentLoading] = useState(true)
  const [agentError, setAgentError] = useState<string | null>(null)

  const [session, setSession] = useState<AgentSession | null>(null)
  const [messages, setMessages] = useState<ChatMsg[]>([])
  const [input, setInput] = useState('')
  const [sending, setSending] = useState(false)
  const [turnError, setTurnError] = useState<string | null>(null)

  // Painel lateral fica `false` por default — só abre quando o user clica
  // em "Memória". Não abrir automático pra preservar layout single-column
  // de quem não usa memória operacional.
  const [memoryPanelOpen, setMemoryPanelOpen] = useState(false)
  const [memoryRecord, setMemoryRecord] = useState<OperationalMemory | null>(null)
  const [memoryLoading, setMemoryLoading] = useState(false)
  const [memoryError, setMemoryError] = useState<string | null>(null)
  const [memoryResetting, setMemoryResetting] = useState(false)

  const abortRef = useRef<AbortController | null>(null)
  const scrollerRef = useRef<HTMLDivElement | null>(null)
  const inputRef = useRef<HTMLTextAreaElement | null>(null)

  // Auto-grow do textarea: cresce conforme o user adiciona linhas, com cap
  // em 5 linhas (line-height + padding lidos do computed style pra não
  // hardcodar números). Acima do cap, scroll vertical aparece.
  useLayoutEffect(() => {
    const ta = inputRef.current
    if (!ta) return
    ta.style.height = 'auto'
    const cs = window.getComputedStyle(ta)
    const lh = parseFloat(cs.lineHeight) || 20
    const pt = parseFloat(cs.paddingTop) || 0
    const pb = parseFloat(cs.paddingBottom) || 0
    const maxH = lh * 5 + pt + pb
    const desired = ta.scrollHeight
    ta.style.height = `${Math.min(desired, maxH)}px`
    ta.style.overflowY = desired > maxH ? 'auto' : 'hidden'
  }, [input])

  // Carrega o agent pra mostrar nome/modelo no header.
  useEffect(() => {
    if (!id) return
    let cancelled = false
    setAgentLoading(true)
    getAgent(id)
      .then((a) => {
        if (!cancelled) setAgent(a)
      })
      .catch((err: unknown) => {
        if (!cancelled) setAgentError(friendlyError(err, 'Não foi possível carregar o agente.'))
      })
      .finally(() => {
        if (!cancelled) setAgentLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [id])

  // Cancela qualquer stream em andamento ao desmontar.
  useEffect(() => {
    return () => {
      abortRef.current?.abort()
    }
  }, [])

  // Auto-scroll pro fim quando mensagens mudam.
  useEffect(() => {
    scrollerRef.current?.scrollTo({
      top: scrollerRef.current.scrollHeight,
      behavior: 'smooth',
    })
  }, [messages])

  const ensureSession = async (): Promise<AgentSession> => {
    if (session) return session
    if (!id) throw new Error('Agent id ausente')
    const created = await createSession(id)
    setSession(created)
    return created
  }

  const handleNewSession = () => {
    abortRef.current?.abort()
    setSession(null)
    setMessages([])
    setInput('')
    setTurnError(null)
  }

  const hasOperationalMemory = !!agent?.operationalMemory?.schema

  // Auto-fetch da memória: dispara quando o painel está aberto e o turn
  // atual terminou (streaming=false na última mensagem assistant). Cobre
  // tanto o trigger inicial de abertura quanto refresh entre turns, sem
  // polling — o middleware grava antes do stream encerrar, então qualquer
  // fetch pós-streaming captura o estado atualizado.
  useEffect(() => {
    if (!memoryPanelOpen || !session || sending) return
    const lastAssistant = [...messages]
      .reverse()
      .find((m): m is AssistantMsg => m.kind === 'assistant')
    if (lastAssistant && lastAssistant.streaming) return
    void fetchMemorySnapshot(session.sessionId)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [memoryPanelOpen, session, sending, messages])

  // Lê o snapshot atual da memória do agente nesta sessão. Chamado pelo
  // useEffect de auto-refresh após cada turn e ao abrir o painel pela
  // primeira vez. Idempotente: re-chamar com mesmo state apenas atualiza
  // o record (sem efeito colateral no servidor).
  const fetchMemorySnapshot = async (sid: string) => {
    if (!id) return
    setMemoryError(null)
    setMemoryLoading(true)
    try {
      const record = await getOperationalMemory(id, sid, 'session')
      setMemoryRecord(record)
    } catch (err) {
      setMemoryError(friendlyError(err, 'Não foi possível carregar a memória.'))
    } finally {
      setMemoryLoading(false)
    }
  }

  const toggleMemoryPanel = () => {
    setMemoryPanelOpen((open) => !open)
  }

  const resetMemory = async () => {
    if (!id || !session) return
    setMemoryResetting(true)
    setMemoryError(null)
    try {
      await deleteOperationalMemory(id, session.sessionId, 'session')
      setMemoryRecord(null)
    } catch (err) {
      setMemoryError(friendlyError(err, 'Não foi possível resetar a memória.'))
    } finally {
      setMemoryResetting(false)
    }
  }

  const handleSend = async () => {
    const text = input.trim()
    if (!text || !id || sending) return

    setInput('')
    setTurnError(null)
    setSending(true)

    const userMsg: UserMsg = { kind: 'user', id: `u-${shortId()}`, text }
    const assistantMsg: AssistantMsg = {
      kind: 'assistant',
      id: `a-${shortId()}`,
      content: '',
      toolCalls: [],
      streaming: true,
    }
    setMessages((prev) => [...prev, userMsg, assistantMsg])

    const controller = new AbortController()
    abortRef.current = controller

    const updateAssistant = (mutator: (msg: AssistantMsg) => AssistantMsg) => {
      setMessages((prev) =>
        prev.map((m) => (m.id === assistantMsg.id && m.kind === 'assistant' ? mutator(m) : m)),
      )
    }

    try {
      const sess = await ensureSession()
      for await (const event of streamRun(id, sess.sessionId, text, controller.signal)) {
        applyEvent(event, updateAssistant)
        if (event.type === 'done' || event.type === 'error') break
      }
      updateAssistant((m) => ({ ...m, streaming: false }))
    } catch (err) {
      if (err instanceof DOMException && err.name === 'AbortError') {
        updateAssistant((m) => ({ ...m, streaming: false }))
        return
      }
      const msg =
        err instanceof ApiError && err.status === 404
          ? 'Sessão expirou. Comece uma nova sessão pra continuar.'
          : friendlyError(err, 'Falha ao enviar mensagem.')
      setTurnError(msg)
      updateAssistant((m) => ({ ...m, streaming: false, errored: true }))
    } finally {
      setSending(false)
      if (abortRef.current === controller) abortRef.current = null
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const modelLabel = useMemo(() => {
    if (!agent?.model) return ''
    return agent.model.predefinedModelId || agent.model.deploymentName || ''
  }, [agent])

  if (agentLoading) {
    return (
      <Card className="mx-auto flex max-w-4xl items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (agentError || !agent) {
    return <ErrorMessage message={agentError ?? 'Agente não encontrado.'} className="mx-auto max-w-4xl" />
  }

  // Largura máxima do container expande quando o painel de memória está
  // aberto pra dar espaço à coluna lateral sem comprimir o chat. Em
  // viewports < lg o painel ocupa largura total (stack vertical) — limite
  // aceitável pra V1, layout principal é desktop.
  const rootMaxWidth = memoryPanelOpen ? 'max-w-6xl' : 'max-w-4xl'

  return (
    <div className={cn('mx-auto flex h-[calc(100vh-9rem)] flex-col', rootMaxWidth)}>
      <div className="mb-4 flex items-center justify-between gap-4">
        <div className="min-w-0">
          <Button
            variant="ghost"
            size="sm"
            leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
            onClick={() => navigate('/agentes')}
            className="-ml-2 mb-1"
          >
            Voltar
          </Button>
          <div className="flex items-center gap-3">
            <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-success/10 text-success">
              <AgentIcon className="h-5 w-5" />
            </div>
            <div className="min-w-0">
              <h1 className="truncate text-2xl font-semibold tracking-tight">{agent.name}</h1>
              {modelLabel && (
                <p className="mt-0.5 font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                  Sandbox · {modelLabel}
                </p>
              )}
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {hasOperationalMemory && (
            <Button
              variant={memoryPanelOpen ? 'primary' : 'ghost'}
              size="sm"
              onClick={toggleMemoryPanel}
              disabled={!session}
            >
              Memória
            </Button>
          )}
          <Button
            variant="secondary"
            size="sm"
            leftIcon={<PlusIcon className="h-4 w-4" />}
            onClick={handleNewSession}
            disabled={sending && !abortRef.current}
          >
            Nova sessão
          </Button>
        </div>
      </div>

      {agent.enabled === false && (
        <Card className="mb-4 border-warning/40 bg-warning/10">
          <p className="text-sm font-medium text-warning">
            Este agente está desabilitado em produção. O sandbox continua funcionando, mas
            invocar este agente em workflows reais resultará em pulo silencioso.
          </p>
        </Card>
      )}

      {/* Split row: a conversa fica flex-1 e o painel lateral ocupa ~420px
          quando aberto. min-h-0 essencial pra que o overflow interno do
          scroller do chat seja delimitado pelo flex parent — sem isso o
          chat empurra o footer pra fora da viewport. */}
      <div className="flex min-h-0 flex-1 gap-4">
        <Card padded={false} className="flex flex-1 flex-col overflow-hidden">
          <div ref={scrollerRef} className="flex-1 space-y-4 overflow-y-auto px-5 py-5">
            {messages.length === 0 ? (
              <EmptyState />
            ) : (
              messages.map((msg) =>
                msg.kind === 'user' ? (
                  <UserBubble key={msg.id} msg={msg} />
                ) : (
                  <AssistantBubble key={msg.id} msg={msg} />
                ),
              )
            )}
          </div>

          {turnError && (
            <div className="border-t border-border px-5 py-2">
              <p className="text-xs text-danger">{turnError}</p>
            </div>
          )}

          <div className="border-t border-border bg-bg-soft/50 px-4 py-3">
            <div className="flex items-end gap-2">
              <textarea
                ref={inputRef}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder="Pergunte algo ao agente…"
                rows={1}
                disabled={sending}
                className={cn(
                  // leading-5 fixa o line-height pro auto-grow ler valor previsível.
                  // Altura é controlada via style.height no useLayoutEffect (cap = 5 linhas).
                  'block flex-1 resize-none rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-5 text-fg placeholder:text-fg-dim',
                  'focus:outline-none focus:ring-2 focus:ring-accent/30 focus:border-accent',
                  'disabled:cursor-not-allowed disabled:opacity-60',
                )}
              />
              <Button
                aria-label="Enviar mensagem"
                onClick={handleSend}
                disabled={!input.trim() || sending}
                loading={sending}
                rightIcon={!sending ? <ArrowRightIcon className="h-4 w-4" /> : undefined}
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

        {memoryPanelOpen && (
          <OperationalMemoryPanel
            record={memoryRecord}
            loading={memoryLoading}
            error={memoryError}
            resetting={memoryResetting}
            onReset={resetMemory}
            onClose={() => setMemoryPanelOpen(false)}
          />
        )}
      </div>
    </div>
  )
}

interface OperationalMemoryPanelProps {
  record: OperationalMemory | null
  loading: boolean
  error: string | null
  resetting: boolean
  onReset: () => void
  onClose: () => void
}

// Painel lateral persistente que mostra o estado atual da memória operacional.
// Atualiza automaticamente após cada turn (via useEffect no parent) — o user
// vê o agente preenchendo o bloco em tempo real, sem precisar abrir/fechar
// nada. Layout segue o pattern do `MemoryPanel` do ChatDeploymentSandbox.
function OperationalMemoryPanel({
  record,
  loading,
  error,
  resetting,
  onReset,
  onClose,
}: OperationalMemoryPanelProps) {
  return (
    <aside className="flex w-[420px] shrink-0 flex-col overflow-hidden rounded-lg border border-border bg-surface">
      <div className="flex items-start justify-between gap-3 border-b border-border px-4 py-3">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold text-fg">Memória operacional</h2>
          <p className="mt-0.5 text-[11px] leading-relaxed text-fg-muted">
            O agente guarda anotações entre as mensagens. Aqui você vê o estado atual desse bloco — atualiza sozinho a cada resposta.
          </p>
        </div>
        <IconButton aria-label="Fechar painel de memória" onClick={onClose} size="sm">
          <CloseIcon className="h-4 w-4" />
        </IconButton>
      </div>

      <div className="flex items-center justify-between gap-2 border-b border-border bg-bg-soft/40 px-4 py-2">
        <span className="text-[11px] text-fg-dim">
          {record
            ? `v${record.version} · atualizada em ${formatMemoryTimestamp(record.updatedAt)}`
            : 'Sem registros pra esta sessão.'}
        </span>
        {record && (
          <Button
            variant="ghost"
            size="sm"
            onClick={onReset}
            loading={resetting}
            className="-mr-1"
          >
            Resetar
          </Button>
        )}
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-3">
        {loading && !record ? (
          <div className="flex items-center justify-center py-10">
            <Spinner className="h-5 w-5 text-fg-muted" />
          </div>
        ) : error ? (
          <ErrorMessage message={error} />
        ) : record ? (
          <pre className="overflow-auto rounded-md border border-border bg-bg-soft p-3 font-mono text-[11px] leading-5 text-fg">
            {JSON.stringify(record.payload, null, 2)}
          </pre>
        ) : (
          <p className="py-8 text-center text-xs text-fg-muted">
            Memória vazia pra esta sessão. O próximo turno do agente cria a primeira versão.
          </p>
        )}
      </div>
    </aside>
  )
}

function formatMemoryTimestamp(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString('pt-BR')
}

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center px-6 py-14 text-center">
      <div className="mb-4 flex h-12 w-12 items-center justify-center rounded-full bg-accent-subtle text-accent">
        <AgentIcon className="h-6 w-6" />
      </div>
      <h3 className="text-base font-semibold text-fg">Nenhuma mensagem ainda</h3>
      <p className="mt-1 max-w-sm text-sm text-fg-muted">
        Envie uma pergunta abaixo pra conversar com o agente em modo sandbox. Ferramentas e
        modelos reais são executados, então cada turno conta no consumo do projeto.
      </p>
    </div>
  )
}

function UserBubble({ msg }: { msg: UserMsg }) {
  return (
    <div className="flex justify-end">
      <div className="max-w-[75%] whitespace-pre-wrap break-words rounded-2xl rounded-br-sm bg-accent px-4 py-2.5 text-sm leading-relaxed text-accent-contrast shadow-soft">
        {msg.text}
      </div>
    </div>
  )
}

function AssistantBubble({ msg }: { msg: AssistantMsg }) {
  const showTyping = msg.streaming && msg.content.length === 0 && msg.toolCalls.length === 0
  // Durante streaming exibimos o cru (chunks parciais não parseiam). No turno
  // final, extractConversationalDisplay reconhece tanto o canônico do
  // Conversational ({ ui_component, message, output }) quanto o legacy do
  // Custom ({ response }), evitando o JSON inteiro vazar pra bolha.
  const display = msg.streaming
    ? { message: msg.content, uiComponent: null, output: undefined, structured: false }
    : extractConversationalDisplay(msg.content)

  return (
    <div className="flex justify-start">
      <div className="flex max-w-[85%] flex-col items-start gap-2">
        {msg.toolCalls.map((call) => (
          <ToolCallChip key={call.id} call={call} />
        ))}
        {(display.message.length > 0 || display.structured || msg.errored || showTyping) && (
          <div
            className={cn(
              'rounded-2xl rounded-bl-sm border px-4 py-2.5 text-sm leading-relaxed shadow-card',
              msg.errored
                ? 'border-danger/30 bg-danger/10 text-fg'
                : 'border-border bg-surface text-fg',
            )}
          >
            {showTyping ? (
              <TypingDots />
            ) : (
              <>
                {display.uiComponent && (
                  <div className="mb-1.5">
                    <UiComponentChip value={display.uiComponent} />
                  </div>
                )}
                <div className="whitespace-pre-wrap break-words">{display.message}</div>
                {display.structured && <OutputDetails value={display.output} />}
              </>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

function ToolCallChip({ call }: { call: ToolCall }) {
  const status = call.error ? 'error' : call.result !== undefined ? 'done' : 'running'
  const label = call.name || 'tool'
  return (
    <details
      className={cn(
        'group max-w-full rounded-md border bg-bg-soft text-xs',
        status === 'error' ? 'border-danger/30' : 'border-border',
      )}
    >
      <summary
        className={cn(
          'flex cursor-pointer items-center gap-2 px-3 py-1.5 select-none',
          status === 'running' ? 'text-accent' : status === 'error' ? 'text-danger' : 'text-fg-muted',
        )}
      >
        {status === 'running' ? (
          <Spinner className="h-3 w-3" />
        ) : (
          <span aria-hidden="true">{status === 'error' ? '⚠' : '✓'}</span>
        )}
        <span className="font-medium">tool: {label}</span>
        {status === 'running' && <span className="text-fg-dim">executando…</span>}
      </summary>
      {(call.result || call.error) && (
        <pre className="max-h-40 overflow-auto border-t border-border bg-surface px-3 py-2 font-mono text-[11px] text-fg">
          {call.error ?? call.result ?? ''}
        </pre>
      )}
    </details>
  )
}

function applyEvent(
  event: StreamEvent,
  updateAssistant: (mutator: (msg: AssistantMsg) => AssistantMsg) => void,
): void {
  switch (event.type) {
    case 'text-delta':
      updateAssistant((m) => ({ ...m, content: m.content + event.delta }))
      break
    case 'tool-start':
      updateAssistant((m) => ({
        ...m,
        toolCalls: [...m.toolCalls, { id: event.toolCallId, name: event.toolName }],
      }))
      break
    case 'tool-result':
      updateAssistant((m) => ({
        ...m,
        toolCalls: m.toolCalls.map((c) =>
          c.id === event.toolCallId ? { ...c, result: event.result } : c,
        ),
      }))
      break
    case 'tool-error':
      updateAssistant((m) => ({
        ...m,
        toolCalls: m.toolCalls.map((c) =>
          c.id === event.toolCallId ? { ...c, error: event.error } : c,
        ),
      }))
      break
    case 'error':
      updateAssistant((m) => ({
        ...m,
        content: m.content || event.message,
        errored: true,
        streaming: false,
      }))
      break
    case 'done':
      break
  }
}
