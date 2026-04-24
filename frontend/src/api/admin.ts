import { get } from './client'
import { useQuery } from '@tanstack/react-query'


export interface ConversationSession {
  conversationId: string
  userId: string
  userType?: string
  workflowId: string
  projectId?: string
  title?: string
  activeExecutionId?: string
  contextClearedAt?: string
  createdAt: string
  lastMessageAt: string
  metadata?: Record<string, string>
}

export interface ConversationPage {
  items: ConversationSession[]
  total: number
  page: number
  pageSize: number
}

export interface AdminConversationParams {
  userId?: string
  workflowId?: string
  projectId?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}


export const KEYS = {
  conversations: (params?: AdminConversationParams) => ['admin', 'conversations', params] as const,
}


export const getAdminConversations = (params?: AdminConversationParams) =>
  get<ConversationPage>('/admin/conversations', params as Record<string, string | number | undefined>)


export function useAdminConversations(params?: AdminConversationParams) {
  return useQuery({
    queryKey: KEYS.conversations(params),
    queryFn: () => getAdminConversations(params),
  })
}
