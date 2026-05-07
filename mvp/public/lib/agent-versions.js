// @ts-check
/**
 * API helpers pra `aihub.agent_versions` (snapshots imutáveis de cada
 * publicação). Substitui mvp/src/api/agentVersions.ts (apenas a leitura;
 * publish/rollback são admin-only).
 */

import { get } from './api.js';

/**
 * @typedef {object} AgentModelSnapshot
 * @property {string} [deploymentName]
 * @property {string | null} [predefinedModelId]
 * @property {number | null} [temperature]
 * @property {number | null} [maxTokens]
 *
 * @typedef {object} AgentProviderSnapshot
 * @property {string} type
 * @property {string} clientType
 * @property {string | null} [endpoint]
 *
 * @typedef {object} AgentToolSnapshot
 * @property {string} type
 * @property {string} [name]
 *
 * @typedef {object} AgentStructuredOutputSnapshot
 * @property {string} [responseFormat]
 * @property {string} [schemaName]
 * @property {string | null} [schemaJson]
 *
 * @typedef {object} AgentVersion
 * @property {string} agentVersionId
 * @property {string} agentDefinitionId
 * @property {number} revision
 * @property {string} status
 * @property {string} createdAt
 * @property {string | null} [createdBy]
 * @property {string | null} [changeReason]
 * @property {string | null} [promptContent]
 * @property {AgentModelSnapshot | null} [model]
 * @property {AgentProviderSnapshot | null} [provider]
 * @property {AgentToolSnapshot[]} [tools]
 * @property {AgentStructuredOutputSnapshot | null} [outputSchema]
 * @property {string} contentHash
 * @property {string | null} [description]
 * @property {boolean} breakingChange
 */

/**
 * @param {string} agentId
 * @returns {Promise<AgentVersion[]>}
 */
export function listAgentVersions(agentId) {
  return get(`/agents/${encodeURIComponent(agentId)}/versions`);
}
