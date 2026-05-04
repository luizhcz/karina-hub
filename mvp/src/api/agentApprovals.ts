import { get, post } from './client'
import type { AgentDraft } from './agentDrafts'
import type { Agent } from './agents'
import type { ApprovalAction, ChangeTier } from './agents'

// Endpoints admin-only — non-admin recebe 403 do AdminGate.

export type AgentApprovalsStatusFilter = 'pending' | 'rejected'

export const listAgentApprovals = (status?: AgentApprovalsStatusFilter) =>
  get<AgentDraft[]>(`/agent-approvals${status ? `?status=${status}` : ''}`)

export const getAgentApproval = (id: string) => get<AgentDraft>(`/agent-approvals/${id}`)

export interface ApproveAgentDraftBody {
  changeReason?: string | null
}

// Backend retorna o agente publicado (AgentResponse) com 201 Created.
export const approveAgentDraft = (id: string, body: ApproveAgentDraftBody = {}) =>
  post<Agent>(`/agent-approvals/${id}/approve`, body)

export interface RejectAgentDraftBody {
  feedback: string
}

// Backend exige feedback com mínimo de 10 caracteres. Retorna o draft atualizado.
export const rejectAgentDraft = (id: string, body: RejectAgentDraftBody) =>
  post<AgentDraft>(`/agent-approvals/${id}/reject`, body)

// Histórico draft-scoped (diferente do agent-scoped getApprovalHistory em agents.ts).
// Útil pra mostrar timeline de Submitted/Resubmitted/Approved/Rejected do draft em si.
export interface DraftApprovalHistoryEntry {
  id: string
  draftId: string
  agentDefinitionId?: string | null
  action: ApprovalAction
  actorUserId: string
  feedback?: string | null
  tier?: ChangeTier | null
  occurredAt: string
}

export const getDraftApprovalHistory = (id: string) =>
  get<DraftApprovalHistoryEntry[]>(`/agent-approvals/${id}/history`)
