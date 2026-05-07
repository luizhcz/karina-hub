// @ts-check
/**
 * API helpers pra agent_drafts. Substitui mvp/src/api/agentDrafts.ts.
 */

import { get, post, put } from './api.js';

/**
 * @typedef {'Draft' | 'PendingApproval' | 'Rejected'} AgentDraftStatus
 *
 * @typedef {object} AgentDraft
 * @property {string} id
 * @property {string} name
 * @property {Record<string, any>} payload
 * @property {string} projectId
 * @property {string} tenantId
 * @property {AgentDraftStatus} status
 * @property {string | null} [rejectionFeedback]
 * @property {string | null} [submittedAt]
 * @property {string} createdAt
 * @property {string} updatedAt
 * @property {string | null} [createdBy]
 * @property {boolean} isEditDraft
 * @property {string | null} [baseAgentId]
 * @property {number | null} [baseRevision]
 *
 * @typedef {object} CreateAgentDraftBody
 * @property {string} [id]
 * @property {Record<string, any>} payload
 *
 * @typedef {object} UpdateAgentDraftBody
 * @property {Record<string, any>} payload
 * @property {string} expectedUpdatedAt
 */

const BASE = '/agent-drafts';

/** @returns {Promise<AgentDraft[]>} */
export function listAgentDrafts() {
  return get(BASE);
}

/** @param {string} id @returns {Promise<AgentDraft>} */
export function getAgentDraft(id) {
  return get(`${BASE}/${encodeURIComponent(id)}`);
}

/** @param {CreateAgentDraftBody} body @returns {Promise<AgentDraft>} */
export function createAgentDraft(body) {
  return post(BASE, body);
}

/** @param {string} id @param {UpdateAgentDraftBody} body @returns {Promise<AgentDraft>} */
export function updateAgentDraft(id, body) {
  return put(`${BASE}/${encodeURIComponent(id)}`, body);
}

/**
 * @param {string} id
 * @returns {Promise<{ autoApproved?: boolean, agentVersionId?: string | null }>}
 */
export function submitAgentDraft(id) {
  return post(`${BASE}/${encodeURIComponent(id)}/submit`, {});
}
