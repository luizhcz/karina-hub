import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface AgentPromptVersion {
  versionId: string
  content: string
  isActive: boolean
}

export interface SavePromptRequest {
  versionId: string
  content: string
}

export interface SetMasterResult {
  agentId: string
  master: string
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  list: (agentId: string) => ['prompts', agentId] as const,
  active: (agentId: string) => ['prompts', agentId, 'active'] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const getPromptVersions = (agentId: string) =>
  get<AgentPromptVersion[]>(`/agents/${agentId}/prompts`)

export const getActivePrompt = (agentId: string) =>
  get<AgentPromptVersion>(`/agents/${agentId}/prompts/active`)

export const savePromptVersion = (agentId: string, body: SavePromptRequest) =>
  post<{ agentId: string; versionId: string }>(`/agents/${agentId}/prompts`, body)

export const setMasterPrompt = (agentId: string, versionId: string) =>
  put<SetMasterResult>(`/agents/${agentId}/prompts/master`, { versionId })

export const deletePromptVersion = (agentId: string, versionId: string) =>
  del(`/agents/${agentId}/prompts/${encodeURIComponent(versionId)}`)

export const clearMasterPrompt = (agentId: string) =>
  del(`/agents/${agentId}/prompts/master`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function usePromptVersions(agentId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.list(agentId),
    queryFn: () => getPromptVersions(agentId),
    enabled: !!agentId && enabled,
  })
}

export function useActivePrompt(agentId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.active(agentId),
    queryFn: () => getActivePrompt(agentId),
    enabled: !!agentId && enabled,
    staleTime: 60_000,
  })
}

export function useSavePromptVersion() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: string; body: SavePromptRequest }) =>
      savePromptVersion(agentId, body),
    onSuccess: (_d, { agentId }) => {
      qc.invalidateQueries({ queryKey: KEYS.list(agentId) })
      qc.invalidateQueries({ queryKey: KEYS.active(agentId) })
    },
  })
}

export function useSetMasterPrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, versionId }: { agentId: string; versionId: string }) =>
      setMasterPrompt(agentId, versionId),
    onSuccess: (_d, { agentId }) => {
      qc.invalidateQueries({ queryKey: KEYS.list(agentId) })
      qc.invalidateQueries({ queryKey: KEYS.active(agentId) })
    },
  })
}

export function useClearMasterPrompt() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId }: { agentId: string }) =>
      clearMasterPrompt(agentId),
    onSuccess: (_d, { agentId }) => {
      qc.invalidateQueries({ queryKey: KEYS.list(agentId) })
      qc.invalidateQueries({ queryKey: KEYS.active(agentId) })
    },
  })
}

export function useDeletePromptVersion() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, versionId }: { agentId: string; versionId: string }) =>
      deletePromptVersion(agentId, versionId),
    onSuccess: (_d, { agentId }) => {
      qc.invalidateQueries({ queryKey: KEYS.list(agentId) })
    },
  })
}
