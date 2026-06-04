import { del, get, post, put } from './client'
import type { Workflow, WorkflowAgentReference } from './workflows'

export interface RouterQuickAction {
  id: string
  routerId: string
  pattern: string
  displayText: string
  intent: string
  description?: string | null
  hasWildcard: boolean
  createdAt: string
  updatedAt: string
}

export interface QuickActionBody {
  pattern: string
  displayText: string
  intent: string
  description?: string | null
}

export const listRouterQuickActions = (routerId: string) =>
  get<RouterQuickAction[]>(`/agents/${routerId}/quick-actions`)

export const createRouterQuickAction = (routerId: string, body: QuickActionBody) =>
  post<RouterQuickAction>(`/agents/${routerId}/quick-actions`, body)

export const updateRouterQuickAction = (routerId: string, id: string, body: QuickActionBody) =>
  put<RouterQuickAction>(`/agents/${routerId}/quick-actions/${id}`, body)

export const deleteRouterQuickAction = (routerId: string, id: string) =>
  del<void>(`/agents/${routerId}/quick-actions/${id}`)

/**
 * Extrai o agentId do agente Router referenciado pelo workflow (role='Router').
 * Retorna null quando workflow não declara um Router (workflow não-chat ou
 * sandbox standalone).
 */
export function extractRouterId(workflow: Workflow | null | undefined): string | null {
  if (!workflow) return null
  const router = (workflow.agents as WorkflowAgentReference[] | undefined)?.find(
    (a) => (a.role ?? '').toLowerCase() === 'router',
  )
  return router?.agentId ?? null
}
