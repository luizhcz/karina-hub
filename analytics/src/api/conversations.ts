import { get } from './client'

// Espelha o shape anônimo de
// src/EfsAiHub.Host.Api/Controllers/ConversationsController.cs:94-102.
// Endpoint é cross-project: não envia x-project-id, valida só identidade.
// Ordem: ascendente por CreatedAt (mais antigas primeiro).

export interface ConversationMessage {
  messageId: string
  role: 'user' | 'assistant' | 'system' | string
  /** Texto da fala. Pode conter fences markdown ```json ...``` em respostas estruturadas. */
  message: string
  /** StructuredOutput do workflow quando o agente retornou JSON tipado. */
  output?: unknown
  createdAt: string
  executionId?: string | null
}

export function getConversationMessages(
  conversationId: string,
  opts: { limit?: number; offset?: number } = {},
  init?: RequestInit,
): Promise<ConversationMessage[]> {
  const limit = opts.limit ?? 200
  const offset = opts.offset ?? 0
  const qs = new URLSearchParams({ limit: String(limit), offset: String(offset) }).toString()
  return get<ConversationMessage[]>(
    `/conversations/${encodeURIComponent(conversationId)}/messages?${qs}`,
    init,
  )
}
