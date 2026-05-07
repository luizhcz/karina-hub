// @ts-check
/**
 * API helpers pra evaluation runs (último run por agente — pra badge na
 * grid de Implantacoes). Substitui parte de mvp/src/api/profileEvaluation.ts.
 */

import { get } from './api.js';

/**
 * @typedef {object} EvalRunSummary
 * @property {string} runId
 * @property {string} status Pending | Running | Completed | Failed | Cancelled
 * @property {string} agentVersionId
 * @property {string} testSetVersionId
 * @property {string} evaluatorConfigVersionId
 * @property {string} triggerSource
 * @property {{ preset?: string, source?: string } | null} [triggerContext]
 * @property {number} casesTotal
 * @property {number} casesCompleted
 * @property {number} casesPassed
 * @property {number} casesFailed
 * @property {number | null} [avgScore]
 * @property {string | null} [startedAt]
 * @property {string | null} [completedAt]
 * @property {string} createdAt
 * @property {string | null} [lastError]
 */

/**
 * @typedef {object} EvalResultDetail
 * @property {string} resultId
 * @property {string} caseId
 * @property {string} evaluatorName
 * @property {number} bindingIndex
 * @property {number} repetitionIndex
 * @property {number | null} score
 * @property {boolean} passed
 * @property {string | null} reason
 * @property {string | null} outputContent
 * @property {string | null} judgeModel
 * @property {number | null} latencyMs
 * @property {number | null} costUsd
 * @property {number | null} inputTokens
 * @property {number | null} outputTokens
 * @property {string} createdAt
 */

/**
 * @param {string} agentId
 * @param {number} [take]
 * @returns {Promise<EvalRunSummary[]>}
 */
export function listEvalRunsByAgent(agentId, take = 1) {
  return get(`/agents/${encodeURIComponent(agentId)}/evaluations/runs?take=${take}`);
}

/**
 * @param {string} runId
 * @returns {Promise<EvalResultDetail[]>}
 */
export function listResultsByRun(runId) {
  return get(`/evaluations/runs/${encodeURIComponent(runId)}/results`);
}
