import { useQuery } from '@tanstack/react-query'
import { get } from './client'

/** Item da lista retornada por GET /api/notifications/agent-breaking-changes. */
export interface AgentBreakingChangeNotification {
  agentId: string
  agentName?: string | null
  agentVersionId: string
  revision: number
  changeReason?: string | null
  createdAt: string
  createdBy?: string | null
}

export const NOTIFICATION_KEYS = {
  agentBreaking: (days: number) => ['notifications', 'agent-breaking-changes', days] as const,
}

export const getAgentBreakingChanges = (days = 7) =>
  get<AgentBreakingChangeNotification[]>('/notifications/agent-breaking-changes', { days })

/**
 * Hook do notification bell. Refetch a cada 60s (alinhado com cache do server)
 * pra UI ficar fresh sem polling agressivo.
 */
export function useAgentBreakingChanges(days = 7, enabled = true) {
  return useQuery({
    queryKey: NOTIFICATION_KEYS.agentBreaking(days),
    queryFn: () => getAgentBreakingChanges(days),
    enabled,
    refetchInterval: 60_000,
    staleTime: 30_000,
  })
}
