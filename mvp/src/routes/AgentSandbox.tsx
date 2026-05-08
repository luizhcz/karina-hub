import { useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { createSession, streamRun, type AgentSession, type StreamEvent } from '../api/agentSessions'
import { ApiError, friendlyError } from '../api/client'
import {
  AgentIcon,
  ArrowLeftIcon,
  ArrowRightIcon,
  Button,
  Card,
  ErrorMessage,
  PlusIcon,
  Spinner,
  cn,
} from '../ui'

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

  return (
    <div className="mx-auto flex h-[calc(100vh-9rem)] max-w-4xl flex-col">
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

      {agent.enabled === false && (
        <Card className="mb-4 border-warning/40 bg-warning/10">
          <p className="text-sm font-medium text-warning">
            Este agente está desabilitado em produção. O sandbox continua funcionando, mas
            invocar este agente em workflows reais resultará em pulo silencioso.
          </p>
        </Card>
      )}

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
    </div>
  )
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

  return (
    <div className="flex justify-start">
      <div className="flex max-w-[85%] flex-col items-start gap-2">
        {msg.toolCalls.map((call) => (
          <ToolCallChip key={call.id} call={call} />
        ))}
        {(msg.content.length > 0 || msg.errored || showTyping) && (
          <div
            className={cn(
              'rounded-2xl rounded-bl-sm border px-4 py-2.5 text-sm leading-relaxed shadow-card',
              msg.errored
                ? 'border-danger/30 bg-danger/10 text-fg'
                : 'border-border bg-surface text-fg',
            )}
          >
            {showTyping ? <TypingDots /> : <div className="whitespace-pre-wrap break-words">{msg.content}</div>}
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

function TypingDots() {
  return (
    <span className="flex h-5 items-center gap-1">
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:0ms]" />
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:150ms]" />
      <span className="h-1.5 w-1.5 rounded-full bg-fg-dim animate-bounce [animation-delay:300ms]" />
    </span>
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
