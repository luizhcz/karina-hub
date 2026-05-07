// @ts-check
/**
 * API helpers pra evaluation runs (último run por agente — pra badge na
 * grid de Implantacoes). Substitui parte de mvp/src/api/profileEvaluation.ts.
 */

import { get, post } from './api.js';
import { getIdentity } from './identity.js';

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

/** @typedef {'basic' | 'medium' | 'advanced'} AutoDeployPreset */

/**
 * @typedef {object} AutoDeployResponse
 * @property {string | null} runId
 * @property {string | null} testSetVersionId
 * @property {string | null} evaluatorConfigVersionId
 * @property {AutoDeployPreset} preset
 * @property {number} caseCount
 * @property {number} estimatedCostUsd
 * @property {number} estimatedDurationSeconds
 * @property {string | null} status
 * @property {boolean} deduplicatedFromExisting
 * @property {boolean} generatorFailed
 *
 * @typedef {object} EvalProgressEvent
 * @property {string} status
 * @property {number} casesTotal
 * @property {number} casesCompleted
 * @property {number} casesPassed
 * @property {number} casesFailed
 * @property {number | null} avgScore
 * @property {number} totalCostUsd
 * @property {number} totalTokens
 * @property {string | null} lastError
 * @property {string | null} startedAt
 * @property {string | null} completedAt
 */

/** @type {Record<AutoDeployPreset, { label: string, cost: string, duration: string }>} */
const PRESET_META = {
  basic:    { label: 'Básica',   cost: '$0',     duration: '<30s' },
  medium:   { label: 'Média',    cost: '~$0.10', duration: '~1min' },
  advanced: { label: 'Avançada', cost: '~$0.50', duration: '~3min' },
};

/** @param {AutoDeployPreset} preset */
export function presetMeta(preset) {
  return PRESET_META[preset];
}

/**
 * @param {string} agentId
 * @param {AutoDeployPreset} preset
 * @param {string} [deployedFromWorkflowId]
 * @returns {Promise<AutoDeployResponse>}
 */
export function runAutoDeploy(agentId, preset, deployedFromWorkflowId) {
  return post(`/agents/${encodeURIComponent(agentId)}/evaluations/auto-deploy`, {
    preset,
    deployedFromWorkflowId,
  });
}

/**
 * @param {string} runId
 * @returns {Promise<EvalRunSummary>}
 */
export function getEvalRun(runId) {
  return get(`/evaluations/runs/${encodeURIComponent(runId)}`);
}

/**
 * Stream SSE de progresso da run via EventSource. Identidade vai por query
 * param (EventSource não envia headers customizados). Fecha em status terminal.
 *
 * @param {string} runId
 * @param {(e: EvalProgressEvent) => void} onProgress
 * @param {(e: EvalProgressEvent) => void} onDone
 * @param {(err: Event | Error) => void} [onError]
 * @returns {() => void} cancel function
 */
export function streamEvalRun(runId, onProgress, onDone, onError) {
  const id = getIdentity();
  const url = id?.projectId
    ? `/api/aihub/evaluations/runs/${encodeURIComponent(runId)}/stream?projectId=${encodeURIComponent(id.projectId)}`
    : `/api/aihub/evaluations/runs/${encodeURIComponent(runId)}/stream`;

  const es = new EventSource(url);

  es.addEventListener('progress', (/** @type {MessageEvent} */ ev) => {
    try { onProgress(JSON.parse(ev.data)); } catch { /* keep-alive */ }
  });

  es.addEventListener('done', (/** @type {MessageEvent} */ ev) => {
    try { onDone(JSON.parse(ev.data)); } catch { /* ignore */ }
    finally { es.close(); }
  });

  es.addEventListener('error', (ev) => {
    onError?.(ev);
    if (es.readyState === EventSource.CLOSED) {
      // Browser desistiu de reconectar.
    }
  });

  return () => es.close();
}
