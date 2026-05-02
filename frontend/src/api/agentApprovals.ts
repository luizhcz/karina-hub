import { get, post } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import type { AgentDraft } from './agentDrafts'
import { DRAFT_KEYS } from './agentDrafts'
import { KEYS as AGENT_KEYS } from './agents'
import type { AgentDef } from './agents'

export type ApprovalListStatus = 'pending' | 'rejected'
export type ApprovalAction = 'Submitted' | 'Resubmitted' | 'Approved' | 'Rejected'

export interface AgentApprovalHistoryEntry {
  id: string
  draftId: string
  action: ApprovalAction
  actorUserId: string
  feedback?: string | null
  occurredAt: string
}

export const APPROVAL_KEYS = {
  all: ['agent-approvals'] as const,
  list: (status: ApprovalListStatus) => ['agent-approvals', 'list', status] as const,
  detail: (id: string) => ['agent-approvals', id] as const,
  history: (id: string) => ['agent-approvals', id, 'history'] as const,
}

export const listApprovals = (status: ApprovalListStatus = 'pending') =>
  get<AgentDraft[]>(`/agent-approvals?status=${status}`)

export const getApproval = (id: string) => get<AgentDraft>(`/agent-approvals/${id}`)

export const approveDraft = (id: string, body?: { changeReason?: string }) =>
  post<AgentDef>(`/agent-approvals/${id}/approve`, body ?? {})

export const rejectDraft = (id: string, body: { feedback: string }) =>
  post<AgentDraft>(`/agent-approvals/${id}/reject`, body)

export const getApprovalHistory = (id: string) =>
  get<AgentApprovalHistoryEntry[]>(`/agent-approvals/${id}/history`)

export function useApprovals(status: ApprovalListStatus = 'pending') {
  return useQuery({
    queryKey: APPROVAL_KEYS.list(status),
    queryFn: () => listApprovals(status),
  })
}

export function useApproval(id: string, enabled = true) {
  return useQuery({
    queryKey: APPROVAL_KEYS.detail(id),
    queryFn: () => getApproval(id),
    enabled: enabled && !!id,
  })
}

export function useApprovalHistory(id: string, enabled = true) {
  return useQuery({
    queryKey: APPROVAL_KEYS.history(id),
    queryFn: () => getApprovalHistory(id),
    enabled: enabled && !!id,
  })
}

export function useApproveDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, changeReason }: { id: string; changeReason?: string }) =>
      approveDraft(id, changeReason ? { changeReason } : undefined),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: APPROVAL_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: AGENT_KEYS.all })
    },
  })
}

export function useRejectDraft() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, feedback }: { id: string; feedback: string }) =>
      rejectDraft(id, { feedback }),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: APPROVAL_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.all })
      qc.invalidateQueries({ queryKey: DRAFT_KEYS.detail(id) })
    },
  })
}
