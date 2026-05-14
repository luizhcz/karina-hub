import { del, get, post } from './client'

/**
 * Chaves canônicas do <code>workflow.metadata</code> escritas pelo backend
 * (ChatSandboxService) e lidas pelo frontend pra distinguir Chat Sandbox de
 * Chat deploy real. Espelho 1-1 das constantes em
 * <code>ChatSandboxMetadata</code> no backend (.NET).
 */
export const ChatSandboxMetadataKeys = {
  SessionId: 'chatSandboxSessionId',
  Kind: 'kind',
  KindChatSandbox: 'chat-sandbox',
} as const

export type ChatSandboxSessionStatus = 'Active' | 'Validated' | 'Expired' | 'Closed'

export interface ChatSandboxSession {
  chatSandboxSessionId: string
  agentId: string
  agentVersionId: string
  workflowId: string
  conversationId: string
  projectId: string
  createdByUserId: string
  createdAt: string
  lastMessageAt?: string | null
  expiresAt: string
  status: ChatSandboxSessionStatus
  validatedAt?: string | null
  validatedByUserId?: string | null
  validationNotes?: string | null
}

export interface ChatSandboxCreateBody {
  agentVersionId?: string | null
}

export const createChatSandboxSession = (agentId: string, body: ChatSandboxCreateBody = {}) =>
  post<ChatSandboxSession>(`/agents/${agentId}/chat-sandbox-sessions`, body)

export const listChatSandboxSessions = (agentId: string, statusFilter?: ChatSandboxSessionStatus) => {
  const qs = statusFilter ? `?status=${statusFilter}` : ''
  return get<ChatSandboxSession[]>(`/agents/${agentId}/chat-sandbox-sessions${qs}`)
}

export const getChatSandboxSession = (sessionId: string) =>
  get<ChatSandboxSession>(`/chat-sandbox-sessions/${sessionId}`)

export const closeChatSandboxSession = (sessionId: string) =>
  del<void>(`/chat-sandbox-sessions/${sessionId}`)

export interface ChatSandboxValidateBody {
  notes?: string | null
}

export const validateChatSandboxSession = (sessionId: string, body: ChatSandboxValidateBody = {}) =>
  post<ChatSandboxSession>(`/chat-sandbox-sessions/${sessionId}/validate`, body)
