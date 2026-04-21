import { useState, useEffect, useRef, useCallback } from 'react'
import { chatApi } from '../api'
import type { ConversationSession, ChatMsg, OutputBoleta, OutputRelatorio, StructuredOutput, WorkflowDef } from '../types'

// ── Helpers ──────────────────────────────────────────────────────────────────

function isOutputBoleta(o: StructuredOutput): o is OutputBoleta {
  return 'boletas' in o || 'command' in o
}

function isOutputRelatorio(o: StructuredOutput): o is OutputRelatorio {
  return 'posicoes' in o
}

function formatBRL(value: number): string {
  return value.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL' })
}

function formatQty(value: number): string {
  return value.toLocaleString('pt-BR', { maximumFractionDigits: 0 })
}

// ── BoletaCard ───────────────────────────────────────────────────────────────

const COMMAND_COLORS: Record<string, string> = {
  review:       'bg-amber-600/20 text-amber-300 border-amber-600/40',
  send_orders:  'bg-emerald-600/20 text-emerald-300 border-emerald-600/40',
  draft:        'bg-[#0C1D38] text-[#7596B8] border-[#254980]',
}

function BoletaCard({ output }: { output: OutputBoleta }) {
  const boletas = output.boletas ?? []
  const commandColor = COMMAND_COLORS[output.command ?? ''] ?? 'bg-[#0C1D38] text-[#7596B8] border-[#254980]'

  return (
    <div className="mt-2 space-y-2 w-full max-w-[480px]">
      <div className="flex items-center gap-2 flex-wrap">
        {output.command && (
          <span className={`px-2 py-0.5 rounded border text-[11px] font-mono font-semibold ${commandColor}`}>
            {output.command}
          </span>
        )}
        {boletas.length > 0 && (
          <span className="text-[11px] text-[#4A6B8A]">{boletas.length} boleta{boletas.length > 1 ? 's' : ''}</span>
        )}
      </div>

      {boletas.length > 0 && (
        <div className="rounded-lg border border-[#1A3357] overflow-hidden text-xs">
          <table className="w-full">
            <thead>
              <tr className="bg-[#081529] text-[#4A6B8A] uppercase tracking-wider text-[10px]">
                <th className="px-3 py-1.5 text-left font-medium">Lado</th>
                <th className="px-3 py-1.5 text-left font-medium">Ticker</th>
                <th className="px-3 py-1.5 text-left font-medium">Qtd</th>
                <th className="px-3 py-1.5 text-left font-medium">Preco</th>
                <th className="px-3 py-1.5 text-left font-medium">Tipo</th>
                <th className="px-3 py-1.5 text-left font-medium">Validade</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-[#0C1D38]">
              {boletas.map((b, i) => {
                const isBuy = b.order_type === 'Buy'
                return (
                  <tr key={i} className="bg-[#04091A]/60 font-mono">
                    <td className="px-3 py-1.5">
                      <span className={`font-bold ${isBuy ? 'text-emerald-400' : 'text-rose-400'}`}>
                        {isBuy ? 'C' : 'V'}
                      </span>
                    </td>
                    <td className="px-3 py-1.5 font-bold text-[#DCE8F5]">{b.ticker ?? '—'}</td>
                    <td className="px-3 py-1.5 text-[#B8CEE5]">{b.quantity ?? '—'}</td>
                    <td className="px-3 py-1.5 text-[#B8CEE5]">
                      {b.priceType === 'L' ? `R$ ${b.priceLimit}` :
                       b.priceType === 'F' ? `R$ ${b.volume}` : '—'}
                    </td>
                    <td className="px-3 py-1.5 text-[#7596B8]">
                      {b.priceType === 'M' ? 'MKT' : b.priceType === 'L' ? 'LMT' : b.priceType === 'F' ? 'FIN' : b.priceType ?? '—'}
                    </td>
                    <td className="px-3 py-1.5 text-[#4A6B8A]">{b.expireTime ?? '—'}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

// ── PositionCard ─────────────────────────────────────────────────────────────

function PositionCard({ output }: { output: OutputRelatorio }) {
  const posicoes = output.posicoes ?? []

  if (output.ui_component === 'position_empty' || posicoes.length === 0) {
    return (
      <div className="mt-2 w-full max-w-[480px] rounded-lg border border-[#1A3357] bg-[#0C1D38] px-4 py-3">
        <div className="flex items-center gap-2 text-[#7596B8] text-sm">
          <span className="text-base">📭</span>
          <span>Nenhuma posicao encontrada</span>
        </div>
      </div>
    )
  }

  const totalVolume = posicoes.reduce((acc, p) => acc + p.financialVolume, 0)

  return (
    <div className="mt-2 space-y-2 w-full max-w-[480px]">
      <div className="flex items-center gap-2">
        <span className="px-2 py-0.5 rounded border border-cyan-600/40 bg-cyan-600/20 text-cyan-300 text-[11px] font-mono font-semibold">
          posicao
        </span>
        <span className="text-[11px] text-[#4A6B8A]">
          {posicoes.length} ativo{posicoes.length > 1 ? 's' : ''} · {formatBRL(totalVolume)}
        </span>
      </div>

      <div className="rounded-lg border border-[#1A3357] overflow-hidden text-xs">
        <table className="w-full">
          <thead>
            <tr className="bg-[#081529] text-[#4A6B8A] uppercase tracking-wider text-[10px]">
              <th className="px-3 py-1.5 text-left font-medium">Ticker</th>
              <th className="px-3 py-1.5 text-right font-medium">Quantidade</th>
              <th className="px-3 py-1.5 text-right font-medium">Volume (BRL)</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[#0C1D38]">
            {posicoes.map((p, i) => (
              <tr key={i} className="bg-[#04091A]/60 font-mono">
                <td className="px-3 py-1.5 font-bold text-cyan-300">{p.ticker}</td>
                <td className="px-3 py-1.5 text-right text-[#B8CEE5]">{formatQty(p.totalQuantity)}</td>
                <td className="px-3 py-1.5 text-right text-[#B8CEE5]">{formatBRL(p.financialVolume)}</td>
              </tr>
            ))}
          </tbody>
          {posicoes.length > 1 && (
            <tfoot>
              <tr className="bg-[#081529] font-mono font-semibold">
                <td className="px-3 py-1.5 text-[#7596B8]">Total</td>
                <td className="px-3 py-1.5 text-right text-[#B8CEE5]">
                  {formatQty(posicoes.reduce((a, p) => a + p.totalQuantity, 0))}
                </td>
                <td className="px-3 py-1.5 text-right text-[#DCE8F5]">{formatBRL(totalVolume)}</td>
              </tr>
            </tfoot>
          )}
        </table>
      </div>
    </div>
  )
}

// ── AssistantOutput (router) ─────────────────────────────────────────────────

function AssistantOutput({ output }: { output: StructuredOutput }) {
  if (isOutputRelatorio(output)) return <PositionCard output={output} />
  if (isOutputBoleta(output))    return <BoletaCard output={output} />
  return null
}

// ── Message bubble ────────────────────────────────────────────────────────────

interface MessageBubbleProps {
  msg: ChatMsg
  streaming?: string
  agentPath?: string[]       // agents seen during streaming (in order)
  onSelectExecution?: (execId: string) => void
}

function MessageBubble({ msg, streaming, agentPath, onSelectExecution }: MessageBubbleProps) {
  const isUser = msg.role === 'user'
  const text = streaming !== undefined ? streaming : msg.message
  const execId = msg.executionId

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'} mb-3`}>
      <div className={`max-w-[80%] ${isUser ? 'items-end' : 'items-start'} flex flex-col gap-1`}>
        {!isUser && (
          <span className="text-[10px] text-[#4A6B8A] font-medium uppercase tracking-wider px-1">Assistente</span>
        )}
        <div className={`rounded-2xl px-4 py-2.5 text-sm leading-relaxed ${
          isUser
            ? 'bg-[#DCE8F5] text-[#04091A] rounded-tr-sm'
            : 'bg-[#0C1D38] text-[#DCE8F5] rounded-tl-sm'
        }`}>
          {text}
          {streaming !== undefined && (
            <span className="inline-block w-1.5 h-3.5 ml-0.5 bg-[#7596B8] animate-pulse rounded-sm align-middle" />
          )}
        </div>
        {!isUser && msg.output && (
          <AssistantOutput output={msg.output} />
        )}
        {/* Execution footer — shown for assistant messages */}
        {!isUser && (execId || (agentPath && agentPath.length > 0)) && (
          <div className="flex items-center gap-1.5 flex-wrap px-1 mt-0.5">
            {agentPath && agentPath.length > 0 && (
              <div className="flex items-center gap-1">
                {agentPath.map((a, i) => (
                  <span key={i} className="flex items-center gap-1">
                    {i > 0 && <span className="text-[#3E5F7D] text-[9px]">→</span>}
                    <span className="px-1.5 py-0.5 rounded bg-[#04091A] border border-[#1A3357] text-[9px] font-mono text-[#4A6B8A]">
                      {a.length > 16 ? a.slice(0, 14) + '…' : a}
                    </span>
                  </span>
                ))}
              </div>
            )}
            {execId && (
              <button
                onClick={() => onSelectExecution?.(execId)}
                className="px-1.5 py-0.5 rounded bg-[#04091A] border border-[#1A3357] hover:border-[#254980] text-[9px] font-mono text-[#3E5F7D] hover:text-[#7596B8] transition-colors"
                title={`Ver execução ${execId}`}
              >
                exec:{execId.slice(0, 8)}
              </button>
            )}
          </div>
        )}
      </div>
    </div>
  )
}

// ── HITL Banner ───────────────────────────────────────────────────────────────

function HitlBanner({ prompt, onRespond }: { prompt: string; onRespond: (text: string) => void }) {
  const [value, setValue] = useState('')
  return (
    <div className="mx-4 mb-3 rounded-xl border border-amber-600/50 bg-amber-950/30 p-3 space-y-2">
      <div className="flex items-center gap-2">
        <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse" />
        <span className="text-xs font-semibold text-amber-300 uppercase tracking-wider">Aguardando sua confirmação</span>
      </div>
      <p className="text-sm text-amber-100">{prompt}</p>
      <div className="flex gap-2">
        <input
          autoFocus
          className="flex-1 bg-[#0C1D38] border border-amber-700/50 rounded-lg px-3 py-1.5 text-sm text-[#DCE8F5] placeholder:text-[#4A6B8A] outline-none focus:border-amber-500"
          placeholder="Sua resposta..."
          value={value}
          onChange={e => setValue(e.target.value)}
          onKeyDown={e => { if (e.key === 'Enter' && value.trim()) { onRespond(value.trim()); setValue('') } }}
        />
        <button
          disabled={!value.trim()}
          onClick={() => { if (value.trim()) { onRespond(value.trim()); setValue('') } }}
          className="px-3 py-1.5 bg-amber-600 hover:bg-amber-500 disabled:opacity-40 rounded-lg text-sm font-medium text-white transition-colors"
        >
          Enviar
        </button>
      </div>
    </div>
  )
}

// ── ChatPanel ─────────────────────────────────────────────────────────────────

interface Props {
  chatWorkflows: WorkflowDef[]
  onExecutionChange?: (info: { executionId: string | null; workflowId: string | null }) => void
  onSelectExecution?: (execId: string) => void
}

export function ChatPanel({ chatWorkflows, onExecutionChange, onSelectExecution }: Props) {
  const [selectedWorkflowId, setSelectedWorkflowId] = useState<string>(
    chatWorkflows[0]?.id ?? ''
  )
  const [userType, setUserType] = useState<'cliente' | 'assessor'>('cliente')
  const [userId, setUserId] = useState('user-1')
  const [conversation, setConversation] = useState<ConversationSession | null>(null)
  const [messages, setMessages] = useState<ChatMsg[]>([])
  const [streamingText, setStreamingText] = useState<string | null>(null)
  const [hitlPrompt, setHitlPrompt] = useState<string | null>(null)
  const [inputValue, setInputValue] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [priorConversations, setPriorConversations] = useState<ConversationSession[]>([])
  // Track agents seen during the current streaming turn (in order of first appearance)
  const [streamAgentPath, setStreamAgentPath] = useState<string[]>([])
  const streamAgentPathRef = useRef<string[]>([])
  const streamRef = useRef<EventSource | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)

  // Fetch prior conversations when userId changes (only on the start screen)
  useEffect(() => {
    if (!userId.trim() || conversation) return
    chatApi.getUserConversations(userId).then(setPriorConversations).catch(() => setPriorConversations([]))
  }, [userId, conversation])

  const loadConversation = useCallback(async (conv: ConversationSession) => {
    setConversation(conv)
    setError(null)
    setStreamingText(null)
    setHitlPrompt(null)
    try {
      const msgs = await chatApi.getMessages(conv.conversationId, 50, 0)
      setMessages(msgs.slice().reverse())
    } catch {
      setMessages([])
    }
  }, [])

  // Auto-scroll to bottom on new content
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, streamingText, hitlPrompt])

  const closeStream = useCallback(() => {
    streamRef.current?.close()
    streamRef.current = null
  }, [])

  const openStream = useCallback((convId: string) => {
    closeStream()
    const es = chatApi.openStream(convId)
    streamRef.current = es

    let accumulated = ''
    streamAgentPathRef.current = []
    setStreamAgentPath([])

    es.addEventListener('token', (e: MessageEvent) => {
      try {
        const data = JSON.parse(e.data) as { text?: string; agentId?: string }
        accumulated += (data.text ?? '')
        setStreamingText(accumulated)
        // Track agent transitions
        if (data.agentId) {
          const path = streamAgentPathRef.current
          if (path[path.length - 1] !== data.agentId) {
            const next = [...path, data.agentId]
            streamAgentPathRef.current = next
            setStreamAgentPath(next)
          }
        }
      } catch { /* ignore */ }
    })

    es.addEventListener('waiting_for_input', (e: MessageEvent) => {
      try {
        const data = JSON.parse(e.data) as { prompt?: string }
        setHitlPrompt(data.prompt ?? 'Aguardando confirmação...')
        setStreamingText(null)
        accumulated = ''
      } catch { /* ignore */ }
    })

    es.addEventListener('message_complete', () => {
      closeStream()
      setStreamingText(null)
      setHitlPrompt(null)
      setSending(false)
      accumulated = ''
      streamAgentPathRef.current = []
      setStreamAgentPath([])
      onExecutionChange?.({ executionId: null, workflowId: null })
      // Reload messages from API to get the persisted assistant message with structured output
      chatApi.getMessages(convId).then(msgs => {
        setMessages(msgs.slice().reverse())
      }).catch(console.error)
    })

    es.addEventListener('error', (e: MessageEvent) => {
      try {
        const data = JSON.parse(e.data) as { message?: string }
        setError(data.message ?? 'Erro desconhecido')
      } catch { /* ignore */ }
      closeStream()
      setStreamingText(null)
      setSending(false)
      accumulated = ''
      streamAgentPathRef.current = []
      setStreamAgentPath([])
      onExecutionChange?.({ executionId: null, workflowId: null })
    })

    es.onerror = () => {
      // Connection closed by server (after workflow_completed) — ignore
      closeStream()
      setSending(false)
    }
  }, [closeStream, onExecutionChange])

  // Cleanup on unmount
  useEffect(() => () => closeStream(), [closeStream])

  const startConversation = async () => {
    setError(null)
    setMessages([])
    setStreamingText(null)
    setHitlPrompt(null)
    setPriorConversations([])
    try {
      const conv = await chatApi.createConversation(userId, userType, selectedWorkflowId || undefined)
      setConversation(conv)
    } catch {
      setError('Erro ao criar conversa.')
    }
  }

  const sendMessage = async (text: string) => {
    if (!conversation || sending || !text.trim()) return
    setSending(true)
    setError(null)

    const userMsg: ChatMsg = {
      messageId: `tmp-${Date.now()}`,
      conversationId: conversation.conversationId,
      role: 'user',
      message: text,
      createdAt: new Date().toISOString(),
    }
    setMessages(prev => [...prev, userMsg])
    setStreamingText('')
    setHitlPrompt(null)
    streamAgentPathRef.current = []
    setStreamAgentPath([])

    try {
      const result = await chatApi.sendMessage(
        conversation.conversationId,
        userId,
        userType,
        [{ role: 'user', message: text }]
      )
      if (result.executionId && !result.hitlResolved) {
        onExecutionChange?.({ executionId: result.executionId, workflowId: conversation.workflowId })
        openStream(conversation.conversationId)
      } else if (result.hitlResolved) {
        // HITL resolved — the existing SSE stream will continue automatically
        // The workflow was already paused; stream is still open
      } else {
        setSending(false)
        setStreamingText(null)
      }
    } catch (e) {
      const err = e instanceof Error ? e.message : 'Erro ao enviar mensagem.'
      setError(err)
      setSending(false)
      setStreamingText(null)
    }
  }

  const handleHitlResponse = (text: string) => {
    setHitlPrompt(null)
    setStreamingText('')
    sendMessage(text)
  }

  const handleSubmit = () => {
    const text = inputValue.trim()
    if (!text) return
    setInputValue('')
    sendMessage(text)
  }

  // ── Render ─────────────────────────────────────────────────────────────────

  if (!conversation) {
    return (
      <div className="flex flex-col h-full items-center justify-center gap-6 p-8">
        <div className="w-16 h-16 rounded-2xl bg-[#0C1D38] border border-[#254980] flex items-center justify-center">
          <span className="text-3xl">💬</span>
        </div>
        <div className="text-center space-y-1">
          <h2 className="text-lg font-semibold text-[#DCE8F5]">Iniciar Chat</h2>
          <p className="text-sm text-[#7596B8]">Selecione o workflow e clique em Nova Conversa</p>
        </div>

        <div className="w-full max-w-xs space-y-3">
          {/* User type toggle */}
          <div className="flex flex-col gap-1">
            <label className="text-[11px] uppercase tracking-wider text-[#4A6B8A] font-medium">Tipo de Usuário</label>
            <div className="flex rounded-lg overflow-hidden border border-[#1A3357]">
              {(['cliente', 'assessor'] as const).map(t => (
                <button
                  key={t}
                  onClick={() => setUserType(t)}
                  className={`flex-1 py-2 text-sm font-medium transition-colors ${
                    userType === t
                      ? 'bg-[#DCE8F5] text-[#04091A]'
                      : 'bg-[#0C1D38] text-[#4A6B8A] hover:text-[#DCE8F5]'
                  }`}
                >
                  {t === 'cliente' ? 'Cliente' : 'Assessor'}
                </button>
              ))}
            </div>
          </div>

          {/* Workflow (optional — server auto-resolves from userType if empty) */}
          <div className="flex flex-col gap-1">
            <label className="text-[11px] uppercase tracking-wider text-[#4A6B8A] font-medium">
              Workflow <span className="normal-case text-[#3E5F7D]">(opcional)</span>
            </label>
            <select
              className="bg-[#081529] border border-[#1A3357] rounded-lg px-3 py-2 text-sm text-[#DCE8F5] outline-none focus:border-[#0057E0]"
              value={selectedWorkflowId}
              onChange={e => setSelectedWorkflowId(e.target.value)}
            >
              <option value="">Auto (pelo tipo de usuário)</option>
              {chatWorkflows.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </div>

          <div className="flex flex-col gap-1">
            <label className="text-[11px] uppercase tracking-wider text-[#4A6B8A] font-medium">
              {userType === 'cliente' ? 'Conta (x-efs-account)' : 'Perfil (x-efs-user-profile-id)'}
            </label>
            <input
              className="bg-[#081529] border border-[#1A3357] rounded-lg px-3 py-2 text-sm text-[#DCE8F5] outline-none focus:border-[#0057E0]"
              value={userId}
              onChange={e => setUserId(e.target.value)}
              placeholder={userType === 'cliente' ? 'conta-123' : 'assessor-456'}
            />
          </div>

          <button
            onClick={startConversation}
            className="w-full py-2.5 bg-[#DCE8F5] hover:bg-white text-[#04091A] rounded-lg text-sm font-semibold transition-colors"
          >
            Nova Conversa
          </button>
        </div>

        {error && <p className="text-xs text-rose-400">{error}</p>}

        {priorConversations.length > 0 && (
          <div className="w-full max-w-xs mt-2">
            <p className="text-[10px] uppercase tracking-wider text-[#4A6B8A] font-medium mb-2">Conversas anteriores</p>
            <div className="space-y-1">
              {priorConversations.slice(0, 8).map(conv => (
                <button
                  key={conv.conversationId}
                  onClick={() => loadConversation(conv)}
                  className="w-full text-left px-3 py-2 rounded-lg bg-[#081529] border border-[#1A3357] hover:border-[#254980] hover:bg-[#0C1D38] transition-colors"
                >
                  <div className="flex items-center justify-between gap-2">
                    <span className="text-xs text-[#DCE8F5] truncate">{conv.workflowId}</span>
                    <span className="text-[10px] text-[#4A6B8A] shrink-0">
                      {new Date(conv.lastMessageAt).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })}
                    </span>
                  </div>
                  <p className="text-[10px] text-[#4A6B8A] font-mono mt-0.5">{conv.conversationId.slice(0, 12)}…</p>
                </button>
              ))}
            </div>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="flex flex-col h-full">
      {/* Conv header */}
      <div className="flex items-center gap-3 px-4 py-2.5 border-b border-[#0C1D38] bg-[#081529] shrink-0">
        <div className="flex-1 min-w-0">
          <p className="text-xs text-[#4A6B8A] truncate">
            {conversation.userType === 'assessor' ? '👔' : '👤'}{' '}
            {conversation.workflowId} · {conversation.conversationId.slice(0, 8)}
          </p>
        </div>
        <button
          onClick={async () => {
            try {
              await chatApi.clearContext(conversation.conversationId)
              setMessages([])
              setStreamingText(null)
              setHitlPrompt(null)
              setError(null)
              closeStream()
            } catch {
              setError('Erro ao limpar conversa.')
            }
          }}
          className="text-xs text-[#4A6B8A] hover:text-rose-400 px-2 py-1 rounded hover:bg-[#0C1D38] transition-colors shrink-0"
          title="Limpa o contexto: próximo workflow não recebe mensagens anteriores"
        >
          Limpar
        </button>
        <button
          onClick={() => { setConversation(null); setMessages([]); closeStream() }}
          className="text-xs text-[#4A6B8A] hover:text-[#B8CEE5] px-2 py-1 rounded hover:bg-[#0C1D38] transition-colors shrink-0"
        >
          + Nova
        </button>
      </div>

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-4 pt-4">
        {messages.length === 0 && !streamingText && (
          <div className="flex flex-col items-center justify-center h-full text-center text-[#4A6B8A] gap-2">
            <p className="text-sm">Digite uma mensagem para começar</p>
            <p className="text-xs text-[#3E5F7D]">Ex: "Compra 100 PETR4 a mercado"</p>
          </div>
        )}

        {messages.map(msg => (
          <MessageBubble
            key={msg.messageId}
            msg={msg}
            onSelectExecution={onSelectExecution}
          />
        ))}

        {streamingText !== null && (
          <MessageBubble
            msg={{ messageId: 'streaming', conversationId: '', role: 'assistant', message: '', createdAt: '' }}
            streaming={streamingText}
            agentPath={streamAgentPath}
          />
        )}

        {error && (
          <div className="mb-3 rounded-lg border border-rose-700/50 bg-rose-950/30 px-4 py-2 text-sm text-rose-300">
            {error}
          </div>
        )}

        <div ref={bottomRef} />
      </div>

      {/* HITL */}
      {hitlPrompt && (
        <HitlBanner prompt={hitlPrompt} onRespond={handleHitlResponse} />
      )}

      {/* Input */}
      <div className="px-4 pb-4 pt-2 shrink-0 border-t border-[#0C1D38]">
        <div className="flex gap-2 items-end">
          <textarea
            rows={1}
            className="flex-1 resize-none bg-[#0C1D38] border border-[#1A3357] rounded-xl px-4 py-2.5 text-sm text-[#DCE8F5] placeholder:text-[#4A6B8A] outline-none focus:border-[#0057E0] focus:ring-1 focus:ring-[#0057E020] transition-colors max-h-32"
            placeholder="Digite sua mensagem..."
            value={inputValue}
            onChange={e => setInputValue(e.target.value)}
            onKeyDown={e => {
              if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault()
                handleSubmit()
              }
            }}
            disabled={sending || !!hitlPrompt}
          />
          <button
            onClick={handleSubmit}
            disabled={sending || !inputValue.trim() || !!hitlPrompt}
            className="px-4 py-2.5 bg-[#DCE8F5] hover:bg-white text-[#04091A] disabled:opacity-40 disabled:cursor-not-allowed rounded-xl text-sm font-semibold transition-all active:scale-95 shrink-0"
          >
            {sending ? '...' : '↑'}
          </button>
        </div>
        <p className="text-[10px] text-[#3E5F7D] mt-1 text-center">Enter para enviar · Shift+Enter para nova linha</p>
      </div>
    </div>
  )
}
