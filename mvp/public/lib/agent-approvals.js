// @ts-check
/**
 * API helpers pra fila de aprovação de drafts. Endpoints admin-only —
 * non-admin recebe 403 (apresentado como mensagem amigável pelo Aprovacoes).
 *
 * Substitui mvp/src/api/agentApprovals.ts.
 */

import { get, post } from './api.js';

/**
 * @typedef {object} AgentDraft
 * @property {string} id
 * @property {string} [name]
 * @property {Record<string, any>} payload Open shape espelhado do backend
 * @property {string} createdAt
 * @property {string} updatedAt
 * @property {string} [createdBy]
 * @property {'Draft' | 'PendingApproval' | 'Rejected'} status
 * @property {string | null} [submittedAt]
 * @property {string | null} [rejectionFeedback]
 * @property {boolean} [isEditDraft]
 * @property {string | null} [baseAgentId]
 * @property {number | null} [baseRevision]
 *
 * @typedef {object} DraftApprovalHistoryEntry
 * @property {string} id
 * @property {string} draftId
 * @property {string | null} [agentDefinitionId]
 * @property {string} action Submitted | Approved | Rejected | Resubmitted
 * @property {string} actorUserId
 * @property {string | null} [feedback]
 * @property {string | null} [tier] Cosmetic | Behavioral
 * @property {string} occurredAt
 */

/**
 * @param {'pending' | 'rejected'} [status]
 * @returns {Promise<AgentDraft[]>}
 */
export function listAgentApprovals(status) {
  return get(`/agent-approvals${status ? `?status=${status}` : ''}`);
}

/**
 * @param {string} id
 * @param {{ changeReason?: string | null }} [body]
 */
export function approveAgentDraft(id, body = {}) {
  return post(`/agent-approvals/${encodeURIComponent(id)}/approve`, body);
}

/**
 * @param {string} id
 * @param {{ feedback: string }} body
 */
export function rejectAgentDraft(id, body) {
  return post(`/agent-approvals/${encodeURIComponent(id)}/reject`, body);
}

/**
 * @param {string} id
 * @returns {Promise<DraftApprovalHistoryEntry[]>}
 */
export function getDraftApprovalHistory(id) {
  return get(`/agent-approvals/${encodeURIComponent(id)}/history`);
}
