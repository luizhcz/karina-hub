// @ts-check
/**
 * API helpers pra `aihub.generic_tools` (HTTP tools que agentes invocam).
 * Substitui mvp/src/api/genericTools.ts.
 */

import { get, post, put } from './api.js';

/**
 * @typedef {object} ParamDefinition
 * @property {string} type
 * @property {string} description
 * @property {boolean} required
 *
 * @typedef {object} GenericTool
 * @property {string} id
 * @property {string} projectId
 * @property {string} tenantId
 * @property {string} name
 * @property {string} description
 * @property {'GET' | 'POST'} httpMethod
 * @property {string} urlTemplate
 * @property {Record<string, ParamDefinition>} pathParams
 * @property {Record<string, ParamDefinition>} queryParams
 * @property {Record<string, string>} customHeaders
 * @property {'None' | 'Json' | 'Text' | 'FormUrlEncoded'} inputContentType
 * @property {string | null} inputSchema
 * @property {'Json' | 'Text' | 'Csv'} outputContentType
 * @property {string | null} outputSchema
 * @property {number | null} timeoutSecondsOverride
 * @property {string | null} whenToUse
 * @property {string} createdAt
 * @property {string} updatedAt
 *
 * @typedef {object} SaveGenericToolBody
 * @property {string} [id]
 * @property {string} name
 * @property {string} description
 * @property {'GET' | 'POST'} httpMethod
 * @property {string} urlTemplate
 * @property {Record<string, ParamDefinition>} pathParams
 * @property {Record<string, ParamDefinition>} queryParams
 * @property {Record<string, string>} customHeaders
 * @property {'None' | 'Json' | 'Text' | 'FormUrlEncoded'} inputContentType
 * @property {string | null} inputSchema
 * @property {'Json' | 'Text' | 'Csv'} outputContentType
 * @property {string | null} outputSchema
 * @property {number | null} timeoutSecondsOverride
 * @property {string | null} whenToUse
 */

/** @returns {Promise<GenericTool[]>} */
export function listGenericTools() {
  return get('/generic-tools');
}

/**
 * @param {string} id
 * @returns {Promise<GenericTool>}
 */
export function getGenericTool(id) {
  return get(`/generic-tools/${encodeURIComponent(id)}`);
}

/**
 * @param {SaveGenericToolBody} body
 * @returns {Promise<GenericTool>}
 */
export function createGenericTool(body) {
  return post('/generic-tools', body);
}

/**
 * @param {string} id
 * @param {SaveGenericToolBody} body
 * @returns {Promise<GenericTool>}
 */
export function updateGenericTool(id, body) {
  return put(`/generic-tools/${encodeURIComponent(id)}`, body);
}

/**
 * Extrai placeholders {nome} de uma URL template. Usado pra auto-detectar
 * pathParams quando o user escreve a URL.
 *
 * @param {string} urlTemplate
 * @returns {string[]}
 */
export function extractPlaceholders(urlTemplate) {
  const placeholders = [];
  const regex = /\{([^/?#{}]+)\}/g;
  let match;
  while ((match = regex.exec(urlTemplate)) !== null) {
    placeholders.push(match[1]);
  }
  return [...new Set(placeholders)];
}
