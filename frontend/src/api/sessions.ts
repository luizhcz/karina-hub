import { get, post, del, apiUrl } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface Session {
  sessionId: string
  agentId: string
  turnCount: number
  createdAt?: string
}

export interface SessionRunResult {
  sessionId: string
  response: string
  turnCount: number
}

export interface SessionStreamHandle {
  source: Promise<Response>
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  list: (agentId: string) => ['sessions', agentId] as const,
  detail: (agentId: string, sessionId: string) => ['sessions', agentId, sessionId] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const getSessions = (agentId: string) =>
  get<Session[]>(`/agents/${agentId}/sessions`)

export const getSession = (agentId: string, sessionId: string) =>
  get<Session>(`/agents/${agentId}/sessions/${sessionId}`)

export const createSession = (agentId: string) =>
  post<Session>(`/agents/${agentId}/sessions`)

export const runSession = (agentId: string, sessionId: string, message: string) =>
  post<SessionRunResult>(`/agents/${agentId}/sessions/${sessionId}/run`, { message })

export const streamSession = (agentId: string, sessionId: string, message: string): SessionStreamHandle => {
  const source = fetch(apiUrl(`/agents/${agentId}/sessions/${sessionId}/stream`), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message }),
  })
  return { source }
}

export const deleteSession = (agentId: string, sessionId: string) =>
  del(`/agents/${agentId}/sessions/${sessionId}`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useSessions(agentId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.list(agentId),
    queryFn: () => getSessions(agentId),
    enabled: !!agentId && enabled,
  })
}

export function useSession(agentId: string, sessionId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.detail(agentId, sessionId),
    queryFn: () => getSession(agentId, sessionId),
    enabled: !!agentId && !!sessionId && enabled,
  })
}

export function useCreateSession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (agentId: string) => createSession(agentId),
    onSuccess: (_d, agentId) => { qc.invalidateQueries({ queryKey: KEYS.list(agentId) }) },
  })
}

export function useRunSession() {
  return useMutation({
    mutationFn: ({ agentId, sessionId, message }: { agentId: string; sessionId: string; message: string }) =>
      runSession(agentId, sessionId, message),
  })
}

export function useDeleteSession() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, sessionId }: { agentId: string; sessionId: string }) =>
      deleteSession(agentId, sessionId),
    onSuccess: (_d, { agentId }) => { qc.invalidateQueries({ queryKey: KEYS.list(agentId) }) },
  })
}
