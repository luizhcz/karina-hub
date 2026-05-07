// @ts-check
/**
 * API helpers pra `aihub.generic_tools` (HTTP tools que agentes invocam).
 * Substitui mvp/src/api/genericTools.ts (apenas os endpoints de listagem
 * por enquanto — Editor é Fase 2).
 */

import { get } from './api.js';

/**
 * @typedef {object} GenericTool
 * @property {string} id
 * @property {string} projectId
 * @property {string} tenantId
 * @property {string} name
 * @property {string} description
 * @property {'GET' | 'POST'} httpMethod
 * @property {string} urlTemplate
 * @property {Record<string, unknown>} pathParams
 * @property {Record<string, unknown>} queryParams
 * @property {Record<string, string>} customHeaders
 * @property {string} inputContentType
 * @property {string | null} inputSchema
 * @property {string} outputContentType
 * @property {string | null} outputSchema
 * @property {number | null} timeoutSecondsOverride
 * @property {string | null} whenToUse
 * @property {string} createdAt
 * @property {string} updatedAt
 */

/** @returns {Promise<GenericTool[]>} */
export function listGenericTools() {
  return get('/generic-tools');
}
