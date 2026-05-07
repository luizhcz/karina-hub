// @ts-check
/**
 * API helpers pra MCP servers (Model Context Protocol). Substitui
 * mvp/src/api/mcpServers.ts (apenas listagem por enquanto).
 *
 * Endpoint backend é /admin/mcp-servers — non-admin recebe via gate aberto
 * pra GET (project-scoped via HasQueryFilter).
 */

import { get, post, put } from './api.js';

/**
 * @typedef {object} McpServer
 * @property {string} id
 * @property {string} name
 * @property {string | null} [description]
 * @property {string} serverLabel
 * @property {string} serverUrl
 * @property {string[]} allowedTools
 * @property {Record<string, string>} headers
 * @property {'never' | 'always'} requireApproval
 * @property {string} projectId
 * @property {string} createdAt
 * @property {string} updatedAt
 */

/**
 * @typedef {object} PagedMcpResponse
 * @property {McpServer[]} items
 * @property {number} total
 * @property {number} page
 * @property {number} pageSize
 */

/** @returns {Promise<McpServer[]>} */
export async function listMcpServers() {
  /** @type {PagedMcpResponse} */
  const page = await get('/admin/mcp-servers?page=1&pageSize=200');
  return page.items;
}

/**
 * @param {string} id
 * @returns {Promise<McpServer>}
 */
export function getMcpServer(id) {
  return get(`/admin/mcp-servers/${encodeURIComponent(id)}`);
}

/**
 * @typedef {object} SaveMcpServerBody
 * @property {string} id
 * @property {string} name
 * @property {string | null} description
 * @property {string} serverLabel
 * @property {string} serverUrl
 * @property {string[]} allowedTools
 * @property {Record<string, string>} headers
 * @property {'never' | 'always'} requireApproval
 */

/**
 * @param {SaveMcpServerBody} body
 * @returns {Promise<McpServer>}
 */
export function createMcpServer(body) {
  return post('/admin/mcp-servers', body);
}

/**
 * @param {string} id
 * @param {SaveMcpServerBody} body
 * @returns {Promise<McpServer>}
 */
export function updateMcpServer(id, body) {
  return put(`/admin/mcp-servers/${encodeURIComponent(id)}`, body);
}
