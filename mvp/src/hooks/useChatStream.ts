import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { applyPatch, type Operation } from 'fast-json-patch'
import { getAuthHeaders } from '../auth/headers'
import { API_BASE_URL } from '../api/baseUrl'

// Hook que consome o stream SSE do AG-UI (POST /chat/ag-ui/stream).
// EventSource só fala GET, então usamos fetch + ReadableStream + parser SSE
// manual. Formato emitido pelo backend (AgUiSseHandler):
//   id: {seq}\n
//   data: {json}\n\n
// Não há campo `event:` — o tipo discriminante vive no JSON em `Type`.

const BASE = API_BASE_URL

export type ChatRole = 'user' | 'assistant' | 'system' | 'tool'

export interface ChatInputMessage {
  role: ChatRole
  content: string
  toolCallId?: string
}

export interface ChatBubble {
  /** ID = messageId do servidor (mesmo que vai pra chat_messages no save).
   *  Único globalmente — o worker gera GUID por step terminal. */
  id: string
  role: ChatRole
  content: string
  complete: boolean
  /** AgentId que produziu a bubble. Vem no metadata dos eventos STEP_* e do
   *  parent dos TOOL_CALL. Permite cruzar com `agentTypeByNodeId` pra trocar
   *  o renderer (ex: card de decisão pro Router em vez de markdown cru). */
  agentId?: string
}

export interface ChatToolCall {
  id: string
  name: string
  args: string
  parentMessageId?: string
  complete: boolean
  result?: string
}

export interface ChatStep {
  id: string
  name: string
  status: 'started' | 'finished'
  /** Timestamp local (ms) em que STEP_STARTED chegou — base pro fallback de
   *  duração quando o backend não envia metadata.durationMs. */
  startedAt: number
  /** Duração em ms do step. Preferência: `metadata.durationMs` do backend
   *  (mais preciso, exclui RTT do SSE); fallback: subtração local entre
   *  STEP_FINISHED e STEP_STARTED. Null enquanto status='started'. */
  durationMs: number | null
  /** Tipo do agente extraído de metadata.agentType (Conversational/Router/...). */
  agentType?: string
}

export type ChatStreamStatus =
  | 'idle'
  | 'streaming'
  | 'completed'
  | 'error'
  | 'cancelled'

export interface UseChatStreamOptions {
  workflowId: string
  /** WorkflowVersionId pra pinar a execução. Vai como header `x-version`.
   *  Empty/null = backend usa estado mutável atual. */
  workflowVersionId?: string | null
}

/** Evento bruto capturado do stream — usado pra debug panel.
 *  Mantém tudo que veio, sem normalizar pra estruturas tipadas. */
export interface ChatRawEvent {
  /** Sequência local, monotônico, atribuído pelo hook (1, 2, 3…). */
  seq: number
  /** Timestamp local de quando o evento foi recebido (ISO). */
  receivedAt: string
  /** ID do servidor (campo `id:` do SSE), se enviado. */
  serverId: string | null
  /** Type extraído do JSON (`type` field). */
  type: string
  /** Payload completo serializável. */
  payload: Record<string, unknown>
}

// Backend AG-UI emite eventos via System.Text.Json com camelCase default
// (JsonSerializerDefaults.Web no Host.Api). Todos os campos abaixo refletem
// EXATAMENTE o wire format — não confundir com a representação PascalCase
// usada no domain C#.
interface AgUiEventBase {
  type: string
  timestamp?: string
}

interface TextMessageStart extends AgUiEventBase {
  type: 'TEXT_MESSAGE_START'
  messageId: string
  role: ChatRole
  agentId?: string
}

interface TextMessageContent extends AgUiEventBase {
  type: 'TEXT_MESSAGE_CONTENT'
  messageId: string
  delta: unknown
  agentId?: string
}

interface TextMessageEnd extends AgUiEventBase {
  type: 'TEXT_MESSAGE_END'
  messageId: string
}

interface ToolCallStart extends AgUiEventBase {
  type: 'TOOL_CALL_START'
  toolCallId: string
  toolCallName: string
  parentMessageId?: string
}

interface ToolCallArgs extends AgUiEventBase {
  type: 'TOOL_CALL_ARGS'
  toolCallId: string
  toolCallName: string
  delta: unknown
}

interface ToolCallEnd extends AgUiEventBase {
  type: 'TOOL_CALL_END'
  toolCallId: string
}

interface ToolCallResult extends AgUiEventBase {
  type: 'TOOL_CALL_RESULT'
  toolCallId: string
  result: string
  messageId?: string
}

interface StepMetadata {
  agentType?: string
  agentName?: string
  durationMs?: number
}

interface StepStarted extends AgUiEventBase {
  type: 'STEP_STARTED'
  stepId: string
  stepName: string
  metadata?: StepMetadata
}

interface StepFinished extends AgUiEventBase {
  type: 'STEP_FINISHED'
  stepId: string
  stepName: string
  metadata?: StepMetadata
}

interface RunStarted extends AgUiEventBase {
  type: 'RUN_STARTED'
  runId: string
  threadId: string
}

interface RunFinished extends AgUiEventBase {
  type: 'RUN_FINISHED'
  runId: string
  threadId: string
  output?: string
}

interface RunError extends AgUiEventBase {
  type: 'RUN_ERROR'
  runId?: string
  error: string
  errorCode?: string
}

interface StateSnapshot extends AgUiEventBase {
  type: 'STATE_SNAPSHOT'
  snapshot: unknown
}

interface StateDelta extends AgUiEventBase {
  type: 'STATE_DELTA'
  delta: Operation[] | unknown
}

interface SafetyViolation extends AgUiEventBase {
  type: 'SAFETY_VIOLATION'
  errorCode?: string
  error?: string
  customValue?: unknown
}

type AgUiEvent =
  | TextMessageStart
  | TextMessageContent
  | TextMessageEnd
  | ToolCallStart
  | ToolCallArgs
  | ToolCallEnd
  | ToolCallResult
  | StepStarted
  | StepFinished
  | RunStarted
  | RunFinished
  | RunError
  | StateSnapshot
  | StateDelta
  | SafetyViolation
  | (AgUiEventBase & { [k: string]: unknown })

// Backend pode mandar Delta como JsonElement (objeto) ou string já serializada;
// pra TEXT_MESSAGE_CONTENT normalizamos pra string concatenável.
function deltaToString(delta: unknown): string {
  if (delta == null) return ''
  if (typeof delta === 'string') return delta
  try {
    return JSON.stringify(delta)
  } catch {
    return ''
  }
}

export interface UseChatStreamResult {
  status: ChatStreamStatus
  threadId: string | null
  bubbles: ChatBubble[]
  toolCalls: ChatToolCall[]
  steps: ChatStep[]
  sharedState: unknown
  errorMessage: string | null
  rawEvents: ChatRawEvent[]
  /** Tipo de cada agente extraído do metadata dos STEP_STARTED/STEP_FINISHED.
   *  Fonte autoritativa (backend conhece AgentDefinition.Type). Quando ausente,
   *  o consumidor pode usar listAgents() como fallback. */
  agentTypeByNodeId: Map<string, string>
  /** Duração em ms de cada step finalizado, indexada por stepId (= nodeId do
   *  workflow). Acumula no turno atual + turnos anteriores — a UI lê o último
   *  para mostrar tag "took 1.2s" perto da bubble do agente. */
  durationMsByStepId: Map<string, number>
  send: (userText: string) => Promise<void>
  cancel: () => Promise<void>
  resolveHitl: (toolCallId: string, response: string) => Promise<void>
  reset: () => void
}

export function useChatStream({
  workflowId,
  workflowVersionId,
}: UseChatStreamOptions): UseChatStreamResult {
  const [status, setStatus] = useState<ChatStreamStatus>('idle')
  const [threadId, setThreadId] = useState<string | null>(null)
  const [bubbles, setBubbles] = useState<ChatBubble[]>([])
  const [toolCalls, setToolCalls] = useState<ChatToolCall[]>([])
  const [steps, setSteps] = useState<ChatStep[]>([])
  const [sharedState, setSharedState] = useState<unknown>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [rawEvents, setRawEvents] = useState<ChatRawEvent[]>([])
  const [agentTypeByNodeId, setAgentTypeByNodeId] = useState<Map<string, string>>(new Map())

  // Backend AG-UI é authoritative do histórico via DB (ResolveHistoryWithEcho:
  // se DB tem mensagens, ignora qualquer echo enviado pelo cliente). Por isso
  // mandamos APENAS a última mensagem do user a cada turn — o backend
  // reconstrói o contexto sozinho. Mandar o histórico inteiro acumulado seria
  // O(n) bandwidth por turno e ignorado downstream.
  const abortRef = useRef<AbortController | null>(null)
  const executionIdRef = useRef<string | null>(null)
  const threadIdRef = useRef<string | null>(null)
  const seqRef = useRef(0)

  // Backend gera messageId único por step terminal (GUID) — bubble.id usa direto
  // o messageId do servidor, sem reconciliação local. Tool calls ainda têm UID
  // local por turn pra evitar colisão se algum backend antigo reusar toolCallId.
  const turnRef = useRef(0)
  const toolCallUidByIdRef = useRef(new Map<string, string>())

  // Cleanup em unmount: aborta o fetch em andamento (não tenta cancelar via
  // POST porque rota /cancel exige executionId — se o user fechar a aba antes
  // do RUN_STARTED, simplesmente abortamos local).
  useEffect(() => () => abortRef.current?.abort(), [])

  function reset() {
    abortRef.current?.abort()
    abortRef.current = null
    executionIdRef.current = null
    threadIdRef.current = null
    seqRef.current = 0
    turnRef.current = 0
    toolCallUidByIdRef.current.clear()
    setStatus('idle')
    setThreadId(null)
    setBubbles([])
    setToolCalls([])
    setSteps([])
    setSharedState(null)
    setErrorMessage(null)
    setRawEvents([])
    setAgentTypeByNodeId(new Map())
  }

  function indexAgentTypeFromStep(nodeId: string | undefined, agentType: string | undefined) {
    if (!nodeId || !agentType) return
    setAgentTypeByNodeId((prev) => {
      if (prev.get(nodeId) === agentType) return prev
      const next = new Map(prev)
      next.set(nodeId, agentType)
      return next
    })
  }

  function applyEvent(evt: AgUiEvent) {
    switch (evt.type) {
      case 'RUN_STARTED': {
        const e = evt as RunStarted
        if (e.threadId) {
          threadIdRef.current = e.threadId
          setThreadId(e.threadId)
        }
        // O backend usa o próprio runId como ExecutionId pra cancel.
        executionIdRef.current = e.runId ?? null
        break
      }
      case 'TEXT_MESSAGE_START': {
        const e = evt as TextMessageStart
        setBubbles((prev) =>
          prev.some((b) => b.id === e.messageId)
            ? prev
            : [...prev, { id: e.messageId, role: e.role ?? 'assistant', content: '', complete: false, agentId: e.agentId }],
        )
        break
      }
      case 'TEXT_MESSAGE_CONTENT': {
        const e = evt as TextMessageContent
        const chunk = deltaToString(e.delta)
        if (!chunk) break
        setBubbles((prev) => {
          const idx = prev.findIndex((b) => b.id === e.messageId)
          if (idx === -1) {
            // Backend pode pular o START — cria bubble on-demand (caso streamed normal
            // onde só vem TEXT_MESSAGE_CONTENT do token batcher).
            return [
              ...prev,
              { id: e.messageId, role: 'assistant', content: chunk, complete: false, agentId: e.agentId },
            ]
          }
          // Bubble já existe — só atualiza content. agentId pode chegar atrasado em
          // chunks subsequentes; preenche se ainda não tem.
          const next = prev.slice()
          next[idx] = {
            ...next[idx],
            content: next[idx].content + chunk,
            agentId: next[idx].agentId ?? e.agentId,
          }
          return next
        })
        break
      }
      case 'TEXT_MESSAGE_END': {
        const e = evt as TextMessageEnd
        setBubbles((prev) => prev.map((b) => (b.id === e.messageId ? { ...b, complete: true } : b)))
        break
      }
      case 'TOOL_CALL_START': {
        const e = evt as ToolCallStart
        const uid = `${e.toolCallId}::t${turnRef.current}`
        toolCallUidByIdRef.current.set(e.toolCallId, uid)
        // parentMessageId vem do backend como o messageId real do bubble pai —
        // bate direto com bubble.id (que agora também é o messageId real). Sem mapeamento.
        setToolCalls((prev) => [
          ...prev,
          {
            id: uid,
            name: e.toolCallName,
            args: '',
            parentMessageId: e.parentMessageId,
            complete: false,
          },
        ])
        break
      }
      case 'TOOL_CALL_ARGS': {
        const e = evt as ToolCallArgs
        const chunk = deltaToString(e.delta)
        const uid = toolCallUidByIdRef.current.get(e.toolCallId)
        if (!uid) break
        setToolCalls((prev) => {
          const idx = prev.findIndex((t) => t.id === uid)
          if (idx === -1) return prev
          const next = prev.slice()
          next[idx] = { ...next[idx], args: next[idx].args + chunk }
          return next
        })
        break
      }
      case 'TOOL_CALL_END': {
        const e = evt as ToolCallEnd
        const uid = toolCallUidByIdRef.current.get(e.toolCallId)
        if (!uid) break
        setToolCalls((prev) => prev.map((t) => (t.id === uid ? { ...t, complete: true } : t)))
        break
      }
      case 'TOOL_CALL_RESULT': {
        const e = evt as ToolCallResult
        const uid = toolCallUidByIdRef.current.get(e.toolCallId)
        if (!uid) break
        setToolCalls((prev) =>
          prev.map((t) => (t.id === uid ? { ...t, result: e.result, complete: true } : t)),
        )
        break
      }
      case 'STEP_STARTED': {
        const e = evt as StepStarted
        setSteps((prev) => [
          ...prev,
          {
            id: e.stepId,
            name: e.stepName,
            status: 'started',
            startedAt: Date.now(),
            durationMs: null,
            agentType: e.metadata?.agentType,
          },
        ])
        indexAgentTypeFromStep(e.stepId, e.metadata?.agentType)
        break
      }
      case 'STEP_FINISHED': {
        const e = evt as StepFinished
        const backendDuration = e.metadata?.durationMs
        setSteps((prev) =>
          prev.map((s) => {
            if (s.id !== e.stepId) return s
            // Preferência: durationMs autoritativo do backend (mede o real
            // tempo de execução do agente, sem RTT do SSE). Fallback: subtração
            // local — pode estar inflado por latência de rede mas é melhor
            // que esconder o sinal.
            const durationMs =
              typeof backendDuration === 'number' ? backendDuration : Date.now() - s.startedAt
            return { ...s, status: 'finished' as const, durationMs }
          }),
        )
        indexAgentTypeFromStep(e.stepId, e.metadata?.agentType)
        break
      }
      case 'STATE_SNAPSHOT': {
        const e = evt as StateSnapshot
        setSharedState(e.snapshot ?? null)
        break
      }
      case 'STATE_DELTA': {
        const e = evt as StateDelta
        const ops = Array.isArray(e.delta) ? (e.delta as Operation[]) : null
        if (!ops || ops.length === 0) break
        setSharedState((prev: unknown) => {
          try {
            // applyPatch muta o doc — clonamos com structuredClone (Node/browser
            // modernos, suportado pelo target ES do MVP).
            const base = prev == null ? {} : structuredClone(prev)
            const { newDocument } = applyPatch(base, ops, true, false)
            return newDocument
          } catch {
            // Patch inválido — mantém estado anterior pra não derrubar a UI.
            return prev
          }
        })
        break
      }
      case 'RUN_FINISHED': {
        // Output consolidado é fallback pra clientes que ignoraram deltas;
        // bubbles já têm o conteúdo. Sem renderização explícita.
        setStatus('completed')
        break
      }
      case 'RUN_ERROR': {
        const e = evt as RunError
        setStatus('error')
        setErrorMessage(e.error ?? e.errorCode ?? 'Erro desconhecido.')
        break
      }
      case 'SAFETY_VIOLATION': {
        const e = evt as SafetyViolation
        setStatus('error')
        setErrorMessage(`Violação de segurança (${e.errorCode ?? 'unknown'}): ${e.error ?? 'bloqueado'}`)
        break
      }
      default:
        // Eventos ignorados em V1: CUSTOM (actor.persisted, executor.lifecycle,
        // ESCALATION), MESSAGES_SNAPSHOT. Não derrubam o stream.
        break
    }
  }

  // Itera eventos SSE de um stream incremental. Cada evento é um bloco
  // separado por linha em branco; campo `data:` pode ocupar múltiplas linhas
  // (concatenadas com \n por spec) — o backend só emite uma.
  async function consumeStream(body: ReadableStream<Uint8Array>) {
    const reader = body.getReader()
    const decoder = new TextDecoder('utf-8')
    let buffer = ''
    try {
      while (true) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        // Aceita tanto \n\n (formato real do backend) quanto \r\n\r\n
        // (defesa caso algum proxy normalize line endings).
        let boundary = nextEventBoundary(buffer)
        while (boundary) {
          const raw = buffer.slice(0, boundary.idx).replace(/\r\n/g, '\n')
          buffer = buffer.slice(boundary.idx + boundary.length)
          parseAndDispatch(raw)
          boundary = nextEventBoundary(buffer)
        }
      }
      // Drena resto (caso final sem \n\n).
      const tail = buffer.replace(/\r\n/g, '\n').trim()
      if (tail.length > 0) parseAndDispatch(tail)
    } finally {
      reader.releaseLock()
    }
  }

  function parseAndDispatch(raw: string) {
    const lines = raw.split('\n')
    const dataLines: string[] = []
    let serverId: string | null = null
    for (const line of lines) {
      if (line.startsWith('data:')) dataLines.push(line.slice(5).trimStart())
      else if (line.startsWith('id:')) serverId = line.slice(3).trim() || null
      // outros campos SSE são ignorados
    }
    if (dataLines.length === 0) return
    const json = dataLines.join('\n')
    let parsed: AgUiEvent
    try {
      parsed = JSON.parse(json) as AgUiEvent
    } catch {
      return // bloco mal-formado — ignora
    }
    // Captura cru pra painel de debug — todo evento entra, mesmo os que
    // applyEvent não despacha (CUSTOM, MESSAGES_SNAPSHOT, etc).
    seqRef.current += 1
    const raw_evt: ChatRawEvent = {
      seq: seqRef.current,
      receivedAt: new Date().toISOString(),
      serverId,
      type: parsed.type ?? 'UNKNOWN',
      payload: parsed as Record<string, unknown>,
    }
    setRawEvents((prev) => [...prev, raw_evt])
    applyEvent(parsed)
  }

  const send = useCallback(
    async (userText: string) => {
      const trimmed = userText.trim()
      if (!trimmed) return
      if (status === 'streaming') return

      // Bumpa turn pra mapeamento de tool calls (que ainda usa UID local).
      // Bubbles assistant não precisam reset — backend gera messageId único por step.
      turnRef.current += 1
      toolCallUidByIdRef.current.clear()

      const userMsgId = `user-t${turnRef.current}-${Date.now()}`
      const userBubble: ChatBubble = {
        id: userMsgId,
        role: 'user',
        content: trimmed,
        complete: true,
      }
      setBubbles((prev) => [...prev, userBubble])

      setStatus('streaming')
      setErrorMessage(null)

      const controller = new AbortController()
      abortRef.current = controller

      const headers: Record<string, string> = {
        'Content-Type': 'application/json',
        Accept: 'text/event-stream',
        ...getAuthHeaders(),
      }
      headers['x-workflow-id'] = workflowId
      // Header `x-version` pina a execução numa WorkflowVersion específica.
      // Empty/whitespace = backend lê o estado mutável atual.
      if (workflowVersionId && workflowVersionId.trim().length > 0) {
        headers['x-version'] = workflowVersionId
      }

      // Payload mínimo: só a última mensagem do user. Backend reconstrói
      // histórico via DB usando o threadId. Mandar histórico acumulado é
      // anti-pattern — ResolveHistoryWithEcho ignora echo quando o DB tem
      // mensagens (conv existente), e em conv nova o primeiro turn tem só
      // essa única mensagem mesmo.
      const body = {
        threadId: threadIdRef.current,
        workflowId,
        messages: [{ role: 'user', content: trimmed }] satisfies ChatInputMessage[],
      }

      try {
        const res = await fetch(`${BASE}/chat/ag-ui/stream`, {
          method: 'POST',
          headers,
          body: JSON.stringify(body),
          signal: controller.signal,
        })
        if (!res.ok || !res.body) {
          const text = await res.text().catch(() => '')
          throw new Error(text || `HTTP ${res.status}`)
        }
        await consumeStream(res.body)
        // Sem RUN_FINISHED explícito (stream encerrou normalmente) — marca como
        // completo a menos que já tenhamos errorado.
        setStatus((prev) => (prev === 'error' ? prev : 'completed'))
      } catch (err) {
        if ((err as Error).name === 'AbortError') {
          setStatus('cancelled')
          return
        }
        setStatus('error')
        setErrorMessage((err as Error).message ?? 'Falha no stream.')
      } finally {
        abortRef.current = null
      }
    },
    [status, workflowId, workflowVersionId],
  )

  const cancel = useCallback(async () => {
    const execId = executionIdRef.current
    abortRef.current?.abort()
    if (!execId) return
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...getAuthHeaders(),
    }
    try {
      await fetch(`${BASE}/chat/ag-ui/cancel`, {
        method: 'POST',
        headers,
        body: JSON.stringify({ executionId: execId }),
      })
    } catch {
      // Cancel best-effort: o abort local já encerrou o stream.
    }
  }, [])

  const resolveHitl = useCallback(async (toolCallId: string, response: string) => {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...getAuthHeaders(),
    }
    await fetch(`${BASE}/chat/ag-ui/resolve-hitl`, {
      method: 'POST',
      headers,
      body: JSON.stringify({ toolCallId, response }),
    })
  }, [])

  // Derivado: map de stepId → durationMs do último STEP_FINISHED (mesmo step
   // pode aparecer múltiplas vezes em turnos sucessivos; mantemos sempre o
   // valor mais recente). Consumidor usa pra renderizar a tag de tempo perto
   // da bubble do agente (bubble.agentId === stepId no caso comum).
  const durationMsByStepId = useMemo(() => {
    const map = new Map<string, number>()
    for (const s of steps) {
      if (s.durationMs != null) map.set(s.id, s.durationMs)
    }
    return map
  }, [steps])

  return {
    status,
    threadId,
    bubbles,
    toolCalls,
    steps,
    sharedState,
    errorMessage,
    rawEvents,
    agentTypeByNodeId,
    durationMsByStepId,
    send,
    cancel,
    resolveHitl,
    reset,
  }
}

// Encontra o próximo limite de evento SSE (\n\n ou \r\n\r\n). Retorna offset
// + tamanho do separador pra avanço correto no slice. null se nenhum boundary
// completo está disponível no buffer ainda.
function nextEventBoundary(buf: string): { idx: number; length: number } | null {
  const a = buf.indexOf('\n\n')
  const b = buf.indexOf('\r\n\r\n')
  if (a === -1 && b === -1) return null
  if (b === -1 || (a !== -1 && a < b)) return { idx: a, length: 2 }
  return { idx: b, length: 4 }
}
