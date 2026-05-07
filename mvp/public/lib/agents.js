// @ts-check
/**
 * API helpers pra agentes publicados. Substitui mvp/src/api/agents.ts
 * (apenas listAgents — o editor é Fase 4).
 */

import { get } from './api.js';

/**
 * @typedef {object} Agent
 * @property {string} id
 * @property {string} name
 * @property {string | null} [description]
 * @property {boolean} enabled
 * @property {string | null} [originProjectId]
 * @property {string} [projectId]
 * @property {string} [visibility]
 * @property {string} createdAt
 * @property {string} updatedAt
 */

/** @returns {Promise<Agent[]>} */
export function listAgents() {
  return get('/agents');
}
