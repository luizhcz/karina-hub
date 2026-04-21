import { get, post, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export type UserType = 'cliente' | 'assessor'

export interface ConversationSession {
  conversationId: string
  userId: string
  userType?: string
  workflowId: string
  title?: string
  activeExecutionId?: string
  contextClearedAt?: string
  createdAt: string
  lastMessageAt: string
  metadata?: Record<string, string>
}

export interface ChatMsg {
  messageId: string
  conversationId: string
  role: 'user' | 'assistant' | 'system'
  message: string
  output?: unknown
  createdAt: string
  executionId?: string
}

export interface ChatSendResult {
  executionId?: string
  hitlResolved: boolean
  messageIds?: string[]
}

export interface ConversationFull {
  conversation: ConversationSession
  messages: ChatMsg[]
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  all: ['conversations'] as const,
  detail: (id: string) => ['conversations', id] as const,
  messages: (id: string) => ['conversations', id, 'messages'] as const,
  full: (id: string) => ['conversations', id, 'full'] as const,
  userConversations: (userId: string) => ['conversations', 'user', userId] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const createConversation = (body?: { workflowId?: string; metadata?: Record<string, string> }) =>
  post<ConversationSession>('/conversations', body)

export const getConversation = (id: string) =>
  get<ConversationSession>(`/conversations/${id}`)

export const getMessages = (id: string, params?: { limit?: number; offset?: number }) =>
  get<ChatMsg[]>(`/conversations/${id}/messages`, params)

export const sendMessage = (
  id: string,
  messages: { role: string; message: string }[],
) => post<ChatSendResult>(`/conversations/${id}/messages`, messages)

export const clearContext = (id: string) => del(`/conversations/${id}/context`)
export const deleteConversation = (id: string) => del(`/conversations/${id}`)

export const getUserConversations = (userId: string) =>
  get<ConversationSession[]>(`/users/${userId}/conversations`)

export const getConversationFull = (id: string) =>
  get<ConversationFull>(`/conversations/${id}/full`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useConversation(id: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.detail(id),
    queryFn: () => getConversation(id),
    enabled,
  })
}

export function useMessages(id: string, params?: { limit?: number; offset?: number }) {
  return useQuery({
    queryKey: [...KEYS.messages(id), params],
    queryFn: () => getMessages(id, params),
  })
}

export function useConversationFull(id: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.full(id),
    queryFn: () => getConversationFull(id),
    enabled,
  })
}

export function useUserConversations(userId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.userConversations(userId),
    queryFn: () => getUserConversations(userId),
    enabled: !!userId && enabled,
  })
}

export function useCreateConversation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body?: { workflowId?: string; metadata?: Record<string, string> }) =>
      createConversation(body),
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useSendMessage() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      id,
      messages,
    }: {
      id: string
      messages: { role: string; message: string }[]
    }) => sendMessage(id, messages),
    onSuccess: (_d, { id }) => { qc.invalidateQueries({ queryKey: KEYS.messages(id) }) },
  })
}

export function useClearContext() {
  return useMutation({ mutationFn: clearContext })
}

export function useDeleteConversation() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteConversation,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}
