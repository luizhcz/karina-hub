import { useState, useRef, useEffect } from 'react'
import { cn } from '../../../shared/utils/cn'
import { Button } from '../../../shared/ui/Button'
import { Badge } from '../../../shared/ui/Badge'
import { getIdentityHeaders } from '../../../api/client'

// ── Tipos ─────────────────────────────────────────────────────────────────────

type Msg =
  | { kind: 'user'; id: string; text: string }
  | { kind: 'assistant'; id: string; content: string; streaming?: boolean }

// ── Bubbles ───────────────────────────────────────────────────────────────────

function UserBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-end">
      <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-br-sm text-sm leading-relaxed bg-accent-blue text-white">
        <p className="whitespace-pre-wrap break-words">{text}</p>
      </div>
    </div>
  )
}

function AssistantBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-start">
      <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-bl-sm text-sm leading-relaxed bg-bg-tertiary text-text-primary border border-border-primary">
        <p className="whitespace-pre-wrap break-words">{text}</p>
      </div>
    </div>
  )
}

function TypingBubble() {
  return (
    <div className="flex justify-start">
      <div className="px-4 py-2.5 rounded-2xl rounded-bl-sm bg-bg-tertiary border border-border-primary">
        <span className="flex gap-1 items-center h-5">
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:0ms]" />
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:150ms]" />
          <span className="w-1.5 h-1.5 rounded-full bg-text-muted animate-bounce [animation-delay:300ms]" />
        </span>
      </div>
    </div>
  )
}

// ── Main Component ────────────────────────────────────────────────────────────

interface SandboxPanelProps {
  agentId: string
  agentName?: string
}

export function SandboxPanel({ agentId, agentName }: SandboxPanelProps) {
  const [sessionId, setSessionId] = useState<string | null>(null)
  const [msgs, setMsgs] = useState<Msg[]>([])
  const [input, setInput] = useState('')
  const [isSending, setIsSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const messagesEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [msgs])

  const handleNewSession = () => {
    setSessionId(null)
    setMsgs([])
    setInput('')
    setError(null)
  }

  const ensureSession = async (): Promise<string | null> => {
    if (sessionId) return sessionId
    const res = await fetch(`/api/agents/${agentId}/sessions`, {
      method: 'POST',
      headers: getIdentityHeaders(),
    })
    if (!res.ok) return null
    const data = await res.json() as { sessionId: string }
    setSessionId(data.sessionId)
    return data.sessionId
  }

  const handleSend = async () => {
    if (!input.trim() || isSending) return

    const text = input.trim()
    setInput('')
    setIsSending(true)
    setError(null)

    const userMsgId = `u-${Date.now()}`
    setMsgs(prev => [...prev, { kind: 'user', id: userMsgId, text }])

    try {
      const sid = await ensureSession()
      if (!sid) throw new Error('Nao foi possivel criar a sessao')

      const assistantId = `a-${Date.now()}`
      setMsgs(prev => [...prev, { kind: 'assistant', id: assistantId, content: '', streaming: true }])

      const response = await fetch(`/api/agents/${agentId}/sessions/${sid}/stream`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...getIdentityHeaders() },
        body: JSON.stringify({ message: text }),
      })

      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      if (!response.body) throw new Error('No stream body')

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split('\n')
        buffer = lines.pop() ?? ''

        for (const line of lines) {
          if (!line.startsWith('data: ')) continue
          const token = line.slice(6)
          if (token === '[DONE]') break

          setMsgs(prev =>
            prev.map(m =>
              m.kind === 'assistant' && m.id === assistantId
                ? { ...m, content: m.content + token }
                : m
            )
          )
        }
      }

      setMsgs(prev =>
        prev.map(m =>
          m.kind === 'assistant' && m.id === assistantId
            ? { ...m, streaming: false }
            : m
        )
      )
    } catch (err) {
      console.error('[SandboxPanel] error', err)
      setError((err as Error).message)
      setMsgs(prev => prev.filter(m => !(m.kind === 'assistant' && m.content === '')))
    } finally {
      setIsSending(false)
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const isEmpty = msgs.length === 0 && !isSending

  return (
    <div className="flex flex-col h-[calc(100vh-14rem)]">
      {/* Header */}
      <div className="flex items-center gap-3 flex-wrap mb-3 flex-none">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h2 className="text-lg font-semibold text-text-primary">
              Playground: {agentName ?? agentId}
            </h2>
            <Badge variant="blue">SANDBOX</Badge>
          </div>
          {sessionId && (
            <p className="text-[10px] text-text-dimmed mt-0.5 font-mono truncate">
              session: {sessionId}
            </p>
          )}
        </div>
        <Button
          variant="secondary"
          size="sm"
          onClick={handleNewSession}
          disabled={isSending}
        >
          Nova Sessao
        </Button>
      </div>

      {/* Chat area */}
      <div className="flex-1 flex flex-col bg-bg-secondary border border-border-primary rounded-xl overflow-hidden min-h-0">
        {/* Messages */}
        <div className="flex-1 overflow-y-auto p-4 flex flex-col gap-3 min-h-0">
          {isEmpty && (
            <div className="flex flex-col items-center justify-center gap-3 text-center h-full">
              <div className="text-4xl text-text-dimmed">&#9655;</div>
              <p className="text-sm text-text-muted">Nenhuma mensagem ainda.</p>
              <p className="text-xs text-text-dimmed max-w-xs">
                Envie uma mensagem para conversar com o agente em modo sandbox.
              </p>
            </div>
          )}

          {msgs.map((msg) => {
            if (msg.kind === 'user') return <UserBubble key={msg.id} text={msg.text} />
            if (msg.kind === 'assistant') {
              return msg.content
                ? <AssistantBubble key={msg.id} text={msg.content} />
                : <TypingBubble key={msg.id} />
            }
            return null
          })}

          {error && (
            <div className="flex justify-start">
              <div className="max-w-[70%] px-4 py-2.5 rounded-2xl rounded-bl-sm text-sm bg-red-500/10 border border-red-500/30 text-red-400">
                Erro: {error}
              </div>
            </div>
          )}

          <div ref={messagesEndRef} />
        </div>

        {/* Input area */}
        <div className="border-t border-border-primary p-3 flex gap-2 items-end flex-none">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Digite uma mensagem... (Enter para enviar, Shift+Enter para nova linha)"
            rows={2}
            disabled={isSending}
            className={cn(
              'flex-1 bg-bg-tertiary border border-border-primary rounded-lg px-3 py-2 text-sm text-text-primary',
              'placeholder:text-text-muted focus:outline-none focus:border-accent-blue resize-none transition-colors',
              isSending && 'opacity-50 cursor-not-allowed'
            )}
          />
          <Button
            variant="primary"
            size="sm"
            onClick={handleSend}
            loading={isSending}
            disabled={!input.trim()}
          >
            Enviar
          </Button>
        </div>
      </div>
    </div>
  )
}
