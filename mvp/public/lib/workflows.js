// @ts-check
/**
 * API helpers pra `aihub.workflow_definitions` + heurísticas de UI pra
 * detectar workflows criados pela tela de Implantações.
 *
 * Substitui mvp/src/api/workflows.ts (apenas a parcela usada pelo
 * Implantacoes — listWorkflows + isAgentDeployment + deployedAgentId).
 */

import { get } from './api.js';

/**
 * @typedef {object} Workflow
 * @property {string} id
 * @property {string} name
 * @property {string | null} [description]
 * @property {string} [version]
 * @property {string} [orchestrationMode]
 * @property {Array<{ agentId: string, role?: string | null }>} [agents]
 * @property {string} [visibility]
 * @property {string | null} [originProjectId]
 * @property {string | null} [originTenantId]
 * @property {Record<string, string> | null} [metadata]
 * @property {string | null} [currentVersionId]
 * @property {number | null} [currentRevision]
 * @property {string} createdAt
 * @property {string} updatedAt
 */

/** @returns {Promise<Workflow[]>} */
export function listWorkflows() {
  return get('/workflows');
}

/**
 * Heurística: workflow nasceu da tela de implantações se id começar com
 * `deploy-` E tem metadata.deployedFromAgentId. Filtra workflows criados
 * por outras vias (admin via API direta).
 *
 * @param {Workflow} workflow
 * @returns {boolean}
 */
export function isAgentDeployment(workflow) {
  if (!workflow.id.startsWith('deploy-')) return false;
  return !!workflow.metadata?.deployedFromAgentId;
}

/**
 * @param {Workflow} workflow
 * @returns {string | null}
 */
export function deployedAgentId(workflow) {
  return workflow.metadata?.deployedFromAgentId ?? null;
}
