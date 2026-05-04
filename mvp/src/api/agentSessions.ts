import { ApiError, post } from './client'
import { getIdentity } from '../stores/identity'

export interface AgentSession {
  sessionId: string
  agentId: string
  turnCount: number
  createdAt: string
  lastAccessedAt: string
}

// Eventos parseados do stream SSE do `/sessions/{sid}/stream`. O backend
// emite frames `data: <chunk>\n\n` onde `<chunk>` pode ser:
//   - `[DONE]` literal (fim do turno) → emite { type: 'done' }
//   - JSON tipado com discriminante `type` (ex.: TOOL_CALL_*) — futuro;
//     hoje o `/sessions/` não emite eventos estruturados, mas o parser
//     aceita por defesa. → emite o tipo correspondente.
//   - Texto cru (delta de tokens da resposta) → emite { type: 'text-delta' }
//
// Cada delta é parte da resposta final do agent. Caller concatena pra montar
// a mensagem completa. Tool calls não chegam pelo stream desse endpoint hoje
// (são gravadas em aihub.tool_invocations mas não emitidas) — caller que
// quiser mostrar precisa de outro fluxo.

export type StreamEvent =
  | { type: 'text-delta'; delta: string }
  | { type: 'tool-start'; toolCallId: string; toolName: string }
  | { type: 'tool-result'; toolCallId: string; toolName: string; result: string }
  | { type: 'tool-error'; toolCallId: string; toolName: string; error: string }
  | { type: 'error'; message: string }
  | { type: 'done' }

export async function createSession(agentId: string): Promise<AgentSession> {
  return post<AgentSession>(`/agents/${agentId}/sessions`, {})
}

// Abre a stream POST do turn e iterativamente devolve os eventos parseados ao
// caller via async generator. Caller usa `for await (const ev of streamRun(...))`
// pra atualizar UI em tempo real. Cancelamento via `AbortSignal`.
export async function* streamRun(
  agentId: string,
  sessionId: string,
  message: string,
  signal?: AbortSignal,
): AsyncGenerator<StreamEvent, void, unknown> {
  const id = getIdentity()
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (id?.account) headers['x-efs-account'] = id.account
  if (id?.projectId) headers['x-efs-project-id'] = id.projectId

  const response = await fetch(`/api/agents/${agentId}/sessions/${sessionId}/stream`, {
    method: 'POST',
    headers,
    body: JSON.stringify({ message }),
    signal,
  })

  if (!response.ok) {
    const errorText = await response.text().catch(() => '')
    throw new ApiError(response.status, errorText || `HTTP ${response.status}`)
  }
  if (!response.body) {
    throw new ApiError(500, 'Stream body vazio.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  // Mapeia o nome de tool em andamento por toolCallId pra correlacionar
  // RESULT/ERROR com START quando o backend não repete o nome.
  const toolNameById = new Map<string, string>()

  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break

      buffer += decoder.decode(value, { stream: true })

      // SSE frames separados por \n\n.
      let frameEnd: number
      while ((frameEnd = buffer.indexOf('\n\n')) !== -1) {
        const frame = buffer.slice(0, frameEnd)
        buffer = buffer.slice(frameEnd + 2)
        const event = parseFrame(frame, toolNameById)
        if (event) yield event
        if (event?.type === 'done') return
      }
    }

    // Trailing buffer (caso a stream feche sem newline final).
    if (buffer.trim().length > 0) {
      const event = parseFrame(buffer, toolNameById)
      if (event) yield event
    }
  } finally {
    try {
      reader.releaseLock()
    } catch {
      // Lock já foi liberado por cancelamento — ignora.
    }
  }
}

function parseFrame(rawFrame: string, toolNameById: Map<string, string>): StreamEvent | null {
  // Cada frame pode conter múltiplas linhas `data: ...`. Junta os payloads
  // preservando newlines internos via '\n' (conforme spec SSE).
  const lines = rawFrame.split('\n')
  const dataParts: string[] = []
  for (const line of lines) {
    if (line.startsWith('data:')) {
      // SSE: tira só o prefixo "data:" + 1 espaço opcional. Não tira mais
      // espaços — eles podem ser significativos no delta de texto.
      let value = line.slice(5)
      if (value.startsWith(' ')) value = value.slice(1)
      dataParts.push(value)
    }
  }
  if (dataParts.length === 0) return null
  const payload = dataParts.join('\n')
  if (payload.length === 0) return null
  if (payload === '[DONE]') return { type: 'done' }

  // Heurística: payload começa com '{' ou '[' → tenta parsear como JSON
  // tipado; se falhar, cai no caminho de texto cru. Tudo que não é JSON
  // estruturado é tratado como delta de texto da resposta.
  const trimmed = payload.trim()
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      const parsed = JSON.parse(trimmed)
      if (typeof parsed === 'object' && parsed !== null && 'type' in parsed) {
        const event = mapTypedEvent(parsed as Record<string, unknown>, toolNameById)
        if (event) return event
      }
      // JSON válido mas sem `type` reconhecido — trata como delta de texto
      // (pode ser parte do output estruturado do agent).
    } catch {
      // Não é JSON completo — fall-through pra text-delta.
    }
  }
  return { type: 'text-delta', delta: payload }
}

function mapTypedEvent(
  obj: Record<string, unknown>,
  toolNameById: Map<string, string>,
): StreamEvent | null {
  const type = String(obj.type ?? '').toUpperCase()
  switch (type) {
    case 'TEXT_MESSAGE_CONTENT':
      return { type: 'text-delta', delta: String(obj.delta ?? '') }
    case 'TOOL_CALL_START': {
      const toolCallId = String(obj.toolCallId ?? '')
      const toolName = String(obj.toolCallName ?? obj.toolName ?? '')
      if (toolCallId) toolNameById.set(toolCallId, toolName)
      return { type: 'tool-start', toolCallId, toolName }
    }
    case 'TOOL_CALL_RESULT': {
      const toolCallId = String(obj.toolCallId ?? '')
      const toolName = toolNameById.get(toolCallId) ?? ''
      const resultRaw = obj.result
      const result = typeof resultRaw === 'string' ? resultRaw : JSON.stringify(resultRaw ?? '')
      return { type: 'tool-result', toolCallId, toolName, result }
    }
    case 'TOOL_CALL_ERROR': {
      const toolCallId = String(obj.toolCallId ?? '')
      const toolName = toolNameById.get(toolCallId) ?? ''
      return {
        type: 'tool-error',
        toolCallId,
        toolName,
        error: String(obj.error ?? 'Tool execution failed'),
      }
    }
    case 'RUN_ERROR':
    case 'ERROR':
      return { type: 'error', message: String(obj.error ?? obj.message ?? 'Erro durante o turno.') }
    default:
      return null
  }
}
