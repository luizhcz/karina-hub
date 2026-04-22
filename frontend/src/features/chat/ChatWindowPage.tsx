import { useState, useEffect } from 'react'
import { useParams, useNavigate, Link } from 'react-router'
import { cn } from '../../shared/utils/cn'
import { Button } from '../../shared/ui/Button'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import {
  useConversationFull,
  useMessages,
  useClearContext,
  useDeleteConversation,
  type ChatMsg,
} from '../../api/chat'
import { getIdentityHeaders, post, ApiError } from '../../api/client'
import { useQueryClient } from '@tanstack/react-query'
import { KEYS } from '../../api/chat'
import type { LocalMsg } from './types'
import { UserBubble } from './components/UserBubble'
import { AssistantBubble } from './components/AssistantBubble'
import { SystemBubble } from './components/SystemBubble'
import { TypingBubble } from './components/TypingBubble'
import { ToolCallCard } from './components/ToolCallCard'
import { ErrorBubble } from './components/ErrorBubble'
import { ApprovalBubble } from './components/ApprovalBubble'
import { ProgressTracker } from './components/ProgressTracker'
import { AgentIndicator } from './components/AgentIndicator'
import { ScrollToBottomFab } from './components/ScrollToBottomFab'
import { SharedStatePanel } from './components/SharedStatePanel'
import { EventTimelinePanel } from './components/EventTimelinePanel'
import { SseHealthIndicator, type SseStatus } from './components/SseHealthIndicator'
import { useSmartScroll } from './hooks/useSmartScroll'
import { useAgUiSharedState } from './hooks/useAgUiSharedState'
import { useAgUiEventTimeline } from './hooks/useAgUiEventTimeline'

// ── Helpers ───────────────────────────────────────────────────────────────────

function tryParseJson(raw: string): Record<string, unknown> {
  try { return JSON.parse(raw) } catch { return {} }
}

type StepMsg = Extract<LocalMsg, { kind: 'step' }>
type GroupedItem = (LocalMsg | { kind: 'step-group'; steps: StepMsg[] })

/** Groups consecutive step messages into step-group entries */
function groupLocalMsgs(msgs: LocalMsg[]): GroupedItem[] {
  const result: GroupedItem[] = []
  let stepBuffer: StepMsg[] = []

  const flushSteps = () => {
    if (stepBuffer.length === 0) return
    if (stepBuffer.length === 1) {
      result.push(stepBuffer[0])
    } else {
      result.push({ kind: 'step-group', steps: [...stepBuffer] })
    }
    stepBuffer = []
  }

  for (const msg of msgs) {
    if (msg.kind === 'step') {
      stepBuffer.push(msg)
    } else {
      flushSteps()
      result.push(msg)
    }
  }
  flushSteps()
  return result
}

// ── Main Component ────────────────────────────────────────────────────────────

export function ChatWindowPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [input, setInput] = useState('')
  const [isSending, setIsSending] = useState(false)
  const [currentRunId, setCurrentRunId] = useState<string | null>(null)
  // Lista local que cresce durante o stream — nunca sobrescreve, só acrescenta
  const [localMsgs, setLocalMsgs] = useState<LocalMsg[]>([])
  const [showClearConfirm, setShowClearConfirm] = useState(false)
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false)
  const [activeAgent, setActiveAgent] = useState<string | null>(null)

  const { agentState, stateTimestamp, changedPaths, handleStateSnapshot, handleStateDelta, clearState } =
    useAgUiSharedState()
  const { events, addEvent, clearEvents } = useAgUiEventTimeline()
  const [sseStatus, setSseStatus] = useState<SseStatus>('idle')

  // Limpa shared state ao trocar de conversa (mas NÃO ao finalizar stream)
  useEffect(() => { clearState() }, [id, clearState])

  const { data, isLoading, error, refetch } = useConversationFull(id ?? '', !!id)
  const { data: messages, refetch: refetchMessages } = useMessages(id ?? '')
  const clearContext = useClearContext()
  const deleteConversation = useDeleteConversation()

  const conversation = data?.conversation
  const persistedMsgs: ChatMsg[] = messages ?? data?.messages ?? []

  const { containerRef, bottomRef, isAtBottom, unreadCount, handleScroll, scrollToBottom } =
    useSmartScroll([persistedMsgs.length, localMsgs.length])

  const makeHeaders = (workflowId?: string | null): Record<string, string> => ({
    'Content-Type': 'application/json',
    ...getIdentityHeaders(),
    ...(workflowId ? { 'x-efs-workflow-id': workflowId } : {}),
  })

  const handleSend = async () => {
    if (!input.trim() || !id || isSending) return

    const text = input.trim()
    setInput('')
    setIsSending(true)
    setCurrentRunId(null)

    // Captura o total antes do envio para detectar novos registros no banco
    const prevMsgCount = persistedMsgs.length

    // 1. Mensagem do usuário aparece imediatamente
    setLocalMsgs([{ kind: 'optimistic-user', id: 'opt-user', text }])
    clearEvents()
    setSseStatus('streaming')

    let refetchDone = false

    try {
      const response = await fetch('/api/chat/ag-ui/stream', {
        method: 'POST',
        headers: makeHeaders(conversation?.workflowId),
        body: JSON.stringify({
          messages: [{ role: 'user', content: text }],
          threadId: id,
        }),
      })

      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      if (!response.body) throw new Error('No stream body')

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      let done = false

      // Buffers locais ao loop de stream — não precisam de estado React
      const toolArgBuffers = new Map<string, string>() // toolCallId → delta acumulado
      const hitlPending = new Set<string>()            // toolCallIds de request_approval

      while (!done) {
        const { done: streamDone, value } = await reader.read()
        if (streamDone) break

        // Parsing por frame SSE (separados por \n\n) para capturar id: e data:
        buffer += decoder.decode(value, { stream: true })
        const frames = buffer.split('\n\n')
        buffer = frames.pop() ?? ''

        for (const frame of frames) {
          if (!frame.trim()) continue

          let dataLine: string | null = null
          for (const line of frame.split('\n')) {
            if (line.startsWith('data: ')) dataLine = line.slice(6).trim()
            // 'id: N' disponível aqui para reconexão futura via Last-Event-ID
          }

          if (!dataLine) continue

          try {
            const evt = JSON.parse(dataLine) as {
              type: string
              runId?: string
              messageId?: string
              delta?: unknown
              toolCallId?: string
              toolCallName?: string
              stepId?: string
              stepName?: string
              error?: string
              snapshot?: unknown
            }

            addEvent(evt.type, evt as Record<string, unknown>)

            if (evt.type === 'RUN_STARTED') {
              setCurrentRunId(evt.runId ?? null)

            } else if (evt.type === 'TEXT_MESSAGE_START') {
              const msgId = evt.messageId ?? `msg_${Date.now()}`
              setLocalMsgs(prev => [...prev, { kind: 'streaming', msgId, content: '' }])

            } else if (evt.type === 'TEXT_MESSAGE_CONTENT') {
              const msgId = evt.messageId
              const chunk = typeof evt.delta === 'string' ? evt.delta : ''
              setLocalMsgs(prev =>
                prev.map(m =>
                  m.kind === 'streaming' && m.msgId === msgId
                    ? { ...m, content: m.content + chunk }
                    : m
                )
              )

            } else if (evt.type === 'STEP_STARTED') {
              const stepId = evt.stepId ?? `step_${Date.now()}`
              const stepName = evt.stepName ?? stepId
              // Detect agent name from step name (e.g. "agent: MyAgent" or ends with "Agent")
              const agentMatch = stepName.match(/^agent[:\s]+(.+)$/i)
              if (agentMatch) {
                setActiveAgent(agentMatch[1].trim())
              } else if (/agent$/i.test(stepName)) {
                setActiveAgent(stepName)
              }
              setLocalMsgs(prev => [...prev, { kind: 'step', stepId, stepName, done: false, timestamp: Date.now() }])

            } else if (evt.type === 'STEP_FINISHED') {
              const stepId = evt.stepId ?? ''
              setLocalMsgs(prev =>
                prev.map(m =>
                  m.kind === 'step' && m.stepId === stepId ? { ...m, done: true } : m
                )
              )

            } else if (evt.type === 'TOOL_CALL_START') {
              const toolCallId = evt.toolCallId ?? ''
              const toolCallName = evt.toolCallName ?? ''
              if (toolCallName === 'request_approval') {
                hitlPending.add(toolCallId)
              } else {
                setLocalMsgs(prev => [...prev, { kind: 'tool-call', toolCallId, toolName: toolCallName, done: false, startedAt: Date.now() }])
              }

            } else if (evt.type === 'TOOL_CALL_ARGS') {
              const toolCallId = evt.toolCallId ?? ''
              const deltaStr = typeof evt.delta === 'string' ? evt.delta : JSON.stringify(evt.delta ?? '')
              toolArgBuffers.set(toolCallId, (toolArgBuffers.get(toolCallId) ?? '') + deltaStr)

            } else if (evt.type === 'TOOL_CALL_END') {
              const toolCallId = evt.toolCallId ?? ''
              const raw = toolArgBuffers.get(toolCallId) ?? '{}'
              toolArgBuffers.delete(toolCallId)

              if (hitlPending.has(toolCallId)) {
                hitlPending.delete(toolCallId)
                const args = tryParseJson(raw) as { question?: string; options?: string[]; interactionType?: string }
                const question = args.question ?? 'Confirmar operação?'
                const options = args.options ?? null
                const interactionType = (args.interactionType ?? 'Approval') as 'Approval' | 'Input' | 'Choice'
                setLocalMsgs(prev => [...prev, { kind: 'approval', toolCallId, question, options, interactionType, createdAt: Date.now() }])
              } else {
                setLocalMsgs(prev =>
                  prev.map(m =>
                    m.kind === 'tool-call' && m.toolCallId === toolCallId
                      ? { ...m, done: true, args: raw, endedAt: Date.now() }
                      : m
                  )
                )
              }

            } else if (evt.type === 'TOOL_CALL_RESULT') {
              const toolCallId = evt.toolCallId ?? ''
              const resultStr = typeof (evt as Record<string, unknown>).result === 'string'
                ? (evt as Record<string, unknown>).result as string
                : JSON.stringify((evt as Record<string, unknown>).result ?? '')
              setLocalMsgs(prev =>
                prev.map(m =>
                  m.kind === 'tool-call' && m.toolCallId === toolCallId
                    ? { ...m, done: true, result: resultStr, endedAt: m.endedAt ?? Date.now() }
                    : m
                )
              )

            } else if (evt.type === 'RUN_FINISHED') {
              setActiveAgent(null)
              done = true
              break

            } else if (evt.type === 'RUN_ERROR') {
              setActiveAgent(null)
              const rawErr = evt.error ?? 'Erro na execução.'
              const errText = typeof rawErr === 'string' ? rawErr : JSON.stringify(rawErr)
              setLocalMsgs(prev => [...prev, { kind: 'error', text: errText }])
              done = true
              break

            } else if (evt.type === 'STATE_SNAPSHOT') {
              handleStateSnapshot(evt.snapshot)

            } else if (evt.type === 'STATE_DELTA') {
              handleStateDelta(evt.delta)

            }
          } catch {
            // frame malformado — ignorar
          }
        }
      }

      // Retry até o backend persistir as mensagens (RUN_FINISHED chega antes do commit)
      for (let attempt = 0; attempt < 6; attempt++) {
        if (attempt > 0) await new Promise((r) => setTimeout(r, 400))
        const result = await refetchMessages()
        if ((result.data?.length ?? 0) > prevMsgCount) break
      }
      await qc.invalidateQueries({ queryKey: KEYS.full(id) })
      setLocalMsgs([])
      refetchDone = true
    } catch (err) {
      console.error('[AG-UI] stream error', err)
      setSseStatus('error')
    } finally {
      setIsSending(false)
      setCurrentRunId(null)
      setSseStatus(prev => prev === 'error' ? prev : 'idle')
      if (!refetchDone) {
        setLocalMsgs([])
      }
    }
  }

  const handleCancel = async () => {
    if (!currentRunId) return
    try {
      await fetch('/api/chat/ag-ui/cancel', {
        method: 'POST',
        headers: makeHeaders(),
        body: JSON.stringify({ executionId: currentRunId }),
      })
    } catch (err) {
      console.error('[AG-UI] cancel error', err)
    }
  }

  const handleApproval = async (toolCallId: string, response: string) => {
    // Otimista: marca localmente antes de confirmar no backend.
    setLocalMsgs(prev =>
      prev.map(m =>
        m.kind === 'approval' && m.toolCallId === toolCallId
          ? { ...m, resolved: response }
          : m
      )
    )

    try {
      // Usa `post` do client.ts (wrapped safeFetch) — extrai ApiError do backend
      // com mensagem real. Não precisa passar `makeHeaders()` porque o client
      // injeta identity headers automaticamente via getIdentityHeaders().
      await post<void>('/chat/ag-ui/resolve-hitl', { toolCallId, response })
    } catch (err) {
      // 404 = CAS perdido no HumanInteractionService.ResolveAsync (C7 do sprint anterior):
      // outro caller/pod já resolveu OU a execução expirou. A UI já está otimista,
      // então marca uma nota visível informando que o estado pode estar dessincronizado.
      if (err instanceof ApiError && err.status === 404) {
        setLocalMsgs(prev =>
          prev.map(m =>
            m.kind === 'approval' && m.toolCallId === toolCallId
              ? { ...m, resolved: response, note: 'Já resolvido por outro operador' }
              : m
          )
        )
      } else {
        // Falha de rede ou erro não-esperado: reverter otimismo seria útil, mas
        // muitas vezes o backend processou ok (falha foi no response path).
        // Por segurança, apenas logar — dashboard de métricas tem HitlResolveConflicts.
        console.error('[AG-UI] resolve-hitl error', err)
      }
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const handleClearContext = () => {
    if (!id) return
    clearContext.mutate(id, {
      onSuccess: () => {
        setShowClearConfirm(false)
        refetch()
      },
    })
  }

  const handleDelete = () => {
    if (!id) return
    deleteConversation.mutate(id, {
      onSuccess: () => navigate('/chat'),
    })
  }

  if (!id) return <ErrorCard message="ID da conversa não encontrado." />
  if (isLoading) return <PageLoader />
  if (error) return <ErrorCard message="Erro ao carregar conversa." onRetry={refetch} />

  const isEmpty = persistedMsgs.length === 0 && localMsgs.length === 0 && !isSending

  return (
    <div className="flex h-[calc(100vh-8rem)] gap-4">
      {/* Chat area */}
      <div className="w-1/2 flex flex-col bg-bg-secondary border border-border-primary rounded-xl overflow-hidden relative">
        {/* Messages */}
        <div ref={containerRef} onScroll={handleScroll} className="flex-1 overflow-y-auto p-4 flex flex-col gap-3">
          {isEmpty && (
            <div className="flex-1 flex items-center justify-center">
              <p className="text-sm text-text-muted">Nenhuma mensagem ainda. Inicie a conversa abaixo.</p>
            </div>
          )}

          {/* Mensagens persistidas no banco */}
          {persistedMsgs.map((msg) => {
            const time = new Date(msg.createdAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })
            const text = typeof msg.message === 'string' ? msg.message : JSON.stringify(msg.message ?? '')
            if (msg.role === 'user') return <UserBubble key={msg.messageId} text={text} time={time} />
            if (msg.role === 'system') return <SystemBubble key={msg.messageId} text={text} />
            return <AssistantBubble key={msg.messageId} text={text} time={time} />
          })}

          {/* Agent indicator */}
          {activeAgent && isSending && <AgentIndicator agentName={activeAgent} />}

          {/* Mensagens locais (in-flight) — agrupadas para progress tracker */}
          {groupLocalMsgs(localMsgs).map((item, i) => {
            if (item.kind === 'step-group') {
              return <ProgressTracker key={`sg-${i}`} steps={item.steps} />
            }
            if (item.kind === 'optimistic-user') {
              return <UserBubble key={item.id} text={item.text} />
            }
            if (item.kind === 'streaming') {
              return item.content
                ? <AssistantBubble key={item.msgId} text={item.content} isStreaming />
                : <TypingBubble key={item.msgId} />
            }
            if (item.kind === 'step') {
              return <ProgressTracker key={`step-${item.stepId}`} steps={[item]} />
            }
            if (item.kind === 'tool-call') {
              return <ToolCallCard key={item.toolCallId} item={item} />
            }
            if (item.kind === 'approval') {
              return <ApprovalBubble key={item.toolCallId} item={item} onResolve={handleApproval} />
            }
            if (item.kind === 'error') {
              return <ErrorBubble key={`err-${i}`} text={item.text} />
            }
            return null
          })}

          {/* Typing indicator — visível enquanto aguarda resposta de texto */}
          {isSending && !localMsgs.some(m => m.kind === 'streaming' || m.kind === 'error') && (
            <TypingBubble />
          )}

          <div ref={bottomRef} />
        </div>

        {/* Scroll-to-bottom FAB */}
        <ScrollToBottomFab visible={!isAtBottom} unreadCount={unreadCount} onClick={scrollToBottom} />

        {/* Input area */}
        <div className="border-t border-border-primary p-3 flex gap-2 items-center">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Digite uma mensagem... (Enter para enviar, Shift+Enter para nova linha)"
            rows={1}
            disabled={isSending}
            className={cn(
              'flex-1 bg-bg-tertiary border border-border-primary rounded-xl px-4 py-2.5 text-sm text-text-primary min-h-[40px] max-h-[120px]',
              'placeholder:text-text-muted focus:outline-none focus:ring-2 focus:ring-accent-blue/50 focus:border-accent-blue resize-none transition-all',
              isSending && 'opacity-50 cursor-not-allowed'
            )}
          />
          {isSending && currentRunId ? (
            <button
              onClick={handleCancel}
              className="flex-shrink-0 w-10 h-10 rounded-xl bg-red-600/20 border border-red-500/30 text-red-400 hover:bg-red-600/30 flex items-center justify-center transition-colors"
              title="Cancelar"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5">
                <path d="M6.28 5.22a.75.75 0 00-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 101.06 1.06L10 11.06l3.72 3.72a.75.75 0 101.06-1.06L11.06 10l3.72-3.72a.75.75 0 00-1.06-1.06L10 8.94 6.28 5.22z" />
              </svg>
            </button>
          ) : (
            <button
              onClick={handleSend}
              disabled={!input.trim() || isSending}
              className={cn(
                'flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all',
                input.trim()
                  ? 'bg-accent-blue text-white hover:bg-accent-blue/80 shadow-lg shadow-accent-blue/25'
                  : 'bg-bg-tertiary text-text-muted border border-border-primary cursor-not-allowed'
              )}
              title="Enviar"
            >
              {isSending ? (
                <span className="w-4 h-4 border-2 border-current border-t-transparent rounded-full animate-spin" />
              ) : (
                <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5">
                  <path d="M3.105 2.289a.75.75 0 00-.826.95l1.414 4.925A1.5 1.5 0 005.135 9.25h6.115a.75.75 0 010 1.5H5.135a1.5 1.5 0 00-1.442 1.086l-1.414 4.926a.75.75 0 00.826.95 28.896 28.896 0 0015.293-7.154.75.75 0 000-1.115A28.897 28.897 0 003.105 2.289z" />
                </svg>
              )}
            </button>
          )}
        </div>
      </div>

      {/* Painel de informações */}
      <aside className="w-1/2 flex flex-col min-h-0">
        {/* Área scrollável: info + botões + estado dos agentes */}
        <div className="flex-1 min-h-0 overflow-y-auto flex flex-col gap-4">
          <div className="bg-bg-secondary border border-border-primary rounded-xl p-4 flex flex-col gap-3">
            <h2 className="text-sm font-semibold text-text-primary">Informações</h2>
            <div className="flex flex-col gap-2">
              <div>
                <p className="text-[10px] text-text-muted uppercase tracking-wide">Conversation ID</p>
                <p className="text-xs font-mono text-text-secondary break-all mt-0.5">
                  {conversation?.conversationId ?? id}
                </p>
              </div>
              <div>
                <p className="text-[10px] text-text-muted uppercase tracking-wide">User ID</p>
                <p className="text-xs text-text-secondary mt-0.5">{conversation?.userId ?? '—'}</p>
              </div>
              <div>
                <p className="text-[10px] text-text-muted uppercase tracking-wide">Workflow</p>
                <p className="text-xs text-text-secondary mt-0.5">{conversation?.workflowId ?? '—'}</p>
              </div>
              <div>
                <p className="text-[10px] text-text-muted uppercase tracking-wide">Mensagens</p>
                <p className="text-xs text-text-secondary mt-0.5">{persistedMsgs.length}</p>
              </div>
              {currentRunId && (
                <div>
                  <p className="text-[10px] text-text-muted uppercase tracking-wide">Run ID</p>
                  <p className="text-xs font-mono text-text-dimmed break-all mt-0.5">{currentRunId}</p>
                </div>
              )}
            </div>
            <div>
              <p className="text-[10px] text-text-muted uppercase tracking-wide">Conexão SSE</p>
              <div className="mt-0.5">
                <SseHealthIndicator status={sseStatus} />
              </div>
            </div>
            {conversation?.activeExecutionId && (
              <Link
                to={`/executions/${conversation.activeExecutionId}`}
                className="text-xs text-accent-blue hover:underline"
              >
                Ver Execução →
              </Link>
            )}
          </div>

          <div className="flex flex-col gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => setShowClearConfirm(true)}
              loading={clearContext.isPending}
            >
              Limpar Contexto
            </Button>
            <Button
              variant="danger"
              size="sm"
              onClick={() => setShowDeleteConfirm(true)}
            >
              Excluir Conversa
            </Button>
          </div>

          {/* Shared state panel — sidebar */}
          {agentState && Object.keys(agentState).length > 0 && (
            <SharedStatePanel
              agentState={agentState}
              changedPaths={changedPaths}
              timestamp={stateTimestamp}
              isStreaming={isSending}
            />
          )}
        </div>

        {/* Event timeline — fixo na parte inferior, sempre visível */}
        <div className="shrink-0 mt-4 max-h-[40%] overflow-y-auto">
          <EventTimelinePanel events={events} isStreaming={isSending} />
        </div>
      </aside>

      <ConfirmDialog
        open={showClearConfirm}
        onClose={() => setShowClearConfirm(false)}
        onConfirm={handleClearContext}
        title="Limpar Contexto"
        message="Tem certeza que deseja limpar o contexto desta conversa? O histórico de mensagens será mantido mas o contexto do agente será reiniciado."
        confirmLabel="Limpar"
        loading={clearContext.isPending}
      />

      <ConfirmDialog
        open={showDeleteConfirm}
        onClose={() => setShowDeleteConfirm(false)}
        onConfirm={handleDelete}
        title="Excluir Conversa"
        message="Tem certeza que deseja excluir esta conversa? Esta ação não pode ser desfeita."
        confirmLabel="Excluir"
        variant="danger"
        loading={deleteConversation.isPending}
      />
    </div>
  )
}
