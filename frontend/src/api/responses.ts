import { get, post } from './client'
import { useQuery, useMutation } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface ResponseJob {
  jobId: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
  result?: unknown
  errorMessage?: string
  createdAt: string
  completedAt?: string
}

export interface CreateResponseRequest {
  input: string
  agentId?: string
  workflowId?: string
  metadata?: Record<string, string>
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  detail: (jobId: string) => ['responses', jobId] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const createResponse = (body: CreateResponseRequest) => post<ResponseJob>('/responses', body)
export const getResponse = (jobId: string) => get<ResponseJob>(`/responses/${jobId}`)
export const cancelResponse = (jobId: string) => post<void>(`/responses/${jobId}/cancel`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useResponse(jobId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.detail(jobId),
    queryFn: () => getResponse(jobId),
    enabled,
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status === 'Pending' || status === 'Running' ? 2000 : false
    },
  })
}

export function useCreateResponse() {
  return useMutation({ mutationFn: createResponse })
}

export function useCancelResponse() {
  return useMutation({ mutationFn: cancelResponse })
}
