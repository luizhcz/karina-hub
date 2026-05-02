import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type {
  AgentMiddlewareConfig,
  AgentResiliencePolicy,
  AgentCostBudget,
  AgentSkillRef,
  AgentStructuredOutput,
  AgentToolDef,
  AgentVisibility,
} from './agents'

/**
 * Payload mutável e parcial do rascunho. Espelha AgentDef em estrutura mas todos
 * os campos são opcionais — invariantes só rodam no publish (backend EnsureInvariants).
 */
export interface AgentDraftPayload {
  name?: string
  description?: string
  model?: { deploymentName: string; temperature?: number; maxTokens?: number }
  provider?: { type?: string; clientType?: string; endpoint?: string }
  fallbackProvider?: { type?: string; endpoint?: string }
  instructions?: string
  tools?: AgentToolDef[]
  structuredOutput?: AgentStructuredOutput
  middlewares?: AgentMiddlewareConfig[]
  resilience?: AgentResiliencePolicy
  costBudget?: AgentCostBudget
  skillRefs?: AgentSkillRef[]
  metadata?: Record<string, string>
  visibility?: AgentVisibility
  allowedProjectIds?: string[] | null
  enabled?: boolean
  regressionTestSetId?: string | null
  regressionEvaluatorConfigVersionId?: string | null
}

export type AgentDraftStatus = 'Draft' | 'PendingApproval' | 'Rejected'

export interface AgentDraft {
  id: string
  name: string
  payload: AgentDraftPayload
  projectId: string
  tenantId: string
  baseAgentId?: string | null
  baseRevision?: number | null
  isEditDraft: boolean
  status: AgentDraftStatus
  rejectionFeedback?: string | null
  submittedAt?: string | null
  createdAt: string
  updatedAt: string
  createdBy?: string | null
}

export const DRAFT_KEYS = {
  all: ['agent-drafts'] as const,
  detail: (id: string) => ['agent-drafts', id] as const,
}

export const getAgentDrafts = () => get<AgentDraft[]>('/agent-drafts')
export const getAgentDraft = (id: string) => get<AgentDraft>(`/agent-drafts/${id}`)

export const createAgentDraft = (body: { id?: string; payload: AgentDraftPayload }) =>
  post<AgentDraft>('/agent-drafts', body)

export const updateAgentDraft = (
  id: string,
  body: { payload: AgentDraftPayload; expectedUpdatedAt: string },
) => put<AgentDraft>(`/agent-drafts/${id}`, body)

export const deleteAgentDraft = (id: string) => del(`/agent-drafts/${id}`)

export const submitAgentDraft = (id: string) =>
  post<AgentDraft>(`/agent-drafts/${id}/submit`, {})

export const createEditDraft = (agentId: string) =>
  post<AgentDraft>(`/agents/${agentId}/edit-draft`, {})

export function useAgentDrafts() {
  return useQuery({ queryKey: DRAFT_KEYS.all, queryFn: getAgentDrafts })
}

export function useAgentDraft(id: string, enabled = true) {
  return useQuery({
    queryKey: DRAFT_KEYS.detail(id),
    queryFn: () => getAgentDraft(id),
    enabled: enabled && !!id,
  })
}

export function useCreateAgentDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createAgentDraft,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
    },
  })
}

export function useUpdateAgentDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({
      id,
      payload,
      expectedUpdatedAt,
    }: {
      id: string
      payload: AgentDraftPayload
      expectedUpdatedAt: string
    }) => updateAgentDraft(id, { payload, expectedUpdatedAt }),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.detail(id) })
    },
  })
}

export function useDeleteAgentDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteAgentDraft,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
    },
  })
}

export function useSubmitAgentDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id }: { id: string }) => submitAgentDraft(id),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: ['agent-approvals'] })
    },
  })
}

export function useCreateEditDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createEditDraft,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
    },
  })
}
