import { get, post } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface HumanInteraction {
  interactionId: string
  executionId: string
  workflowId: string
  prompt: string
  context?: string
  status: 'Pending' | 'Resolved' | 'Rejected'
  resolution?: string
  createdAt: string
  resolvedAt?: string
}

export interface ResolveInteractionRequest {
  response: string
  approved: boolean
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  pending: ['interactions', 'pending'] as const,
  detail: (id: string) => ['interactions', id] as const,
  byExecution: (executionId: string) => ['interactions', 'execution', executionId] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const getPendingInteractions = () => get<HumanInteraction[]>('/interactions/pending')
export const getInteraction = (id: string) => get<HumanInteraction>(`/interactions/${id}`)
export const resolveInteraction = (id: string, body: ResolveInteractionRequest) =>
  post<void>(`/interactions/${id}/resolve`, body)
export const getInteractionsByExecution = (executionId: string) =>
  get<HumanInteraction[]>(`/interactions/by-execution/${executionId}`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function usePendingInteractions() {
  return useQuery({ queryKey: KEYS.pending, queryFn: getPendingInteractions })
}

export function useInteraction(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getInteraction(id), enabled })
}

export function useInteractionsByExecution(executionId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.byExecution(executionId),
    queryFn: () => getInteractionsByExecution(executionId),
    enabled,
  })
}

export function useResolveInteraction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResolveInteractionRequest }) => resolveInteraction(id, body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: KEYS.pending })
    },
  })
}
