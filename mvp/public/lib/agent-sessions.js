// @ts-check
/**
 * API helpers pra agent sessions + streaming. Substitui
 * mvp/src/api/agentSessions.ts.
 *
 * O backend expõe SSE via POST com body (texto da mensagem) — EventSource
 * nativo do browser só faz GET sem body, então usamos fetch + ReadableStream
 * + TextDecoder manual. Para clientes que NÃO podem fazer SSE (sem stream
 * reading), o backend tem fallback /run-async + GET /events?since=N
 * documentado no devportal.
 */

import { ApiError, post } from './api.js';
import { getIdentity } from './identity.js';

/**
 * @typedef {object} AgentSession
 * @property {string} sessionId
 * @property {string} agentId
 * @property {number} turnCount
 * @property {string} createdAt
 * @property {string} lastAccessedAt
 *
 * @typedef {{ type: 'text-delta', delta: string }} TextDelta
 * @typedef {{ type: 'tool-start', toolCallId: string, toolName: string }} ToolStart
 * @typedef {{ type: 'tool-result', toolCallId: string, toolName: string, result: string }} ToolResult
 * @typedef {{ type: 'tool-error', toolCallId: string, toolName: string, error: string }} ToolError
 * @typedef {{ type: 'error', message: string }} StreamError
 * @typedef {{ type: 'done' }} StreamDone
 *
 * @typedef {TextDelta | ToolStart | ToolResult | ToolError | StreamError | StreamDone} StreamEvent
 */

/**
 * @param {string} agentId
 * @returns {Promise<AgentSession>}
 */
export function createSession(agentId) {
  return post(`/agents/${encodeURIComponent(agentId)}/sessions`, {});
}

/**
 * Abre stream POST + parseia SSE frames como async generator.
 * Caller usa `for await (const ev of streamRun(...))`. Cancelamento via
 * `AbortSignal`.
 *
 * @param {string} agentId
 * @param {string} sessionId
 * @param {string} message
 * @param {AbortSignal} [signal]
 * @returns {AsyncGenerator<StreamEvent, void, unknown>}
 */
export async function* streamRun(agentId, sessionId, message, signal) {
  const id = getIdentity();
  /** @type {Record<string, string>} */
  const headers = { 'Content-Type': 'application/json' };
  if (id?.account) headers['x-efs-account'] = id.account;
  if (id?.projectId) headers['x-efs-project-id'] = id.projectId;

  const response = await fetch(
    `/api/aihub/agents/${encodeURIComponent(agentId)}/sessions/${encodeURIComponent(sessionId)}/stream`,
    {
      method: 'POST',
      headers,
      body: JSON.stringify({ message }),
      signal,
    },
  );

  if (!response.ok) {
    const errorText = await response.text().catch(() => '');
    throw new ApiError(response.status, errorText || `HTTP ${response.status}`);
  }
  if (!response.body) {
    throw new ApiError(500, 'Stream body vazio.');
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  /** @type {Map<string, string>} */
  const toolNameById = new Map();

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });

      let frameEnd;
      while ((frameEnd = buffer.indexOf('\n\n')) !== -1) {
        const frame = buffer.slice(0, frameEnd);
        buffer = buffer.slice(frameEnd + 2);
        const event = parseFrame(frame, toolNameById);
        if (event) yield event;
        if (event?.type === 'done') return;
      }
    }

    if (buffer.trim().length > 0) {
      const event = parseFrame(buffer, toolNameById);
      if (event) yield event;
    }
  } finally {
    try {
      reader.releaseLock();
    } catch {
      // Lock já liberado por cancelamento.
    }
  }
}

/**
 * @param {string} rawFrame
 * @param {Map<string, string>} toolNameById
 * @returns {StreamEvent | null}
 */
function parseFrame(rawFrame, toolNameById) {
  const lines = rawFrame.split('\n');
  /** @type {string[]} */
  const dataParts = [];
  for (const line of lines) {
    if (line.startsWith('data:')) {
      let value = line.slice(5);
      if (value.startsWith(' ')) value = value.slice(1);
      dataParts.push(value);
    }
  }
  if (dataParts.length === 0) return null;
  const payload = dataParts.join('\n');
  if (payload.length === 0) return null;
  if (payload === '[DONE]') return { type: 'done' };

  const trimmed = payload.trim();
  if (trimmed.startsWith('{') || trimmed.startsWith('[')) {
    try {
      const parsed = JSON.parse(trimmed);
      if (typeof parsed === 'object' && parsed !== null && 'type' in parsed) {
        const event = mapTypedEvent(parsed, toolNameById);
        if (event) return event;
      }
    } catch {
      // Fall-through pra text-delta.
    }
  }
  return { type: 'text-delta', delta: payload };
}

/**
 * @param {any} obj
 * @param {Map<string, string>} toolNameById
 * @returns {StreamEvent | null}
 */
function mapTypedEvent(obj, toolNameById) {
  const type = String(obj.type ?? '').toUpperCase();
  switch (type) {
    case 'TEXT_MESSAGE_CONTENT':
      return { type: 'text-delta', delta: String(obj.delta ?? '') };
    case 'TOOL_CALL_START': {
      const toolCallId = String(obj.toolCallId ?? '');
      const toolName = String(obj.toolCallName ?? obj.toolName ?? '');
      if (toolCallId) toolNameById.set(toolCallId, toolName);
      return { type: 'tool-start', toolCallId, toolName };
    }
    case 'TOOL_CALL_RESULT': {
      const toolCallId = String(obj.toolCallId ?? '');
      const toolName = toolNameById.get(toolCallId) ?? '';
      const resultRaw = obj.result;
      const result = typeof resultRaw === 'string' ? resultRaw : JSON.stringify(resultRaw ?? '');
      return { type: 'tool-result', toolCallId, toolName, result };
    }
    case 'TOOL_CALL_ERROR': {
      const toolCallId = String(obj.toolCallId ?? '');
      const toolName = toolNameById.get(toolCallId) ?? '';
      return {
        type: 'tool-error',
        toolCallId,
        toolName,
        error: String(obj.error ?? 'Tool execution failed'),
      };
    }
    case 'RUN_ERROR':
    case 'ERROR':
      return { type: 'error', message: String(obj.error ?? obj.message ?? 'Erro durante o turno.') };
    default:
      return null;
  }
}
