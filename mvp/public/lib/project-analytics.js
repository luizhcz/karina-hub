// @ts-check
/**
 * API helpers pra analytics do projeto. Substitui
 * mvp/src/api/projectAnalytics.ts.
 */

import { get } from './api.js';

/**
 * @typedef {object} AgentMiniRow
 * @property {string} agentId
 * @property {number} totalTokens
 * @property {number} costUsd
 * @property {number} calls
 *
 * @typedef {object} ProjectOverview
 * @property {string} projectId
 * @property {string} periodFrom
 * @property {string} periodTo
 * @property {number} totalCostUsd
 * @property {number} totalTokens
 * @property {number} totalCalls
 * @property {number} totalExecutions
 * @property {number} completed
 * @property {number} failed
 * @property {number} successRate
 * @property {AgentMiniRow[]} topAgents
 *
 * @typedef {object} ProjectTimeseriesBucket
 * @property {string} bucket
 * @property {number} costUsd
 * @property {number} tokens
 * @property {number} calls
 * @property {number} executions
 * @property {number} completed
 * @property {number} failed
 *
 * @typedef {object} ProjectAgentBreakdown
 * @property {string} agentId
 * @property {string | null} [agentName]
 * @property {string | null} [modelId]
 * @property {number} calls
 * @property {number} totalTokens
 * @property {number} costUsd
 * @property {number} avgDurationMs
 * @property {number} p95DurationMs
 * @property {number} errorRate
 *
 * @typedef {object} ProjectBudgetStatus
 * @property {string} projectId
 * @property {number | null} [maxTokensPerDay]
 * @property {number | null} [maxCostUsdPerDay]
 * @property {number} todayTokens
 * @property {number} todayCostUsd
 * @property {number | null} [tokensUsagePct]
 * @property {number | null} [costUsagePct]
 * @property {boolean} exceeded
 */

/**
 * @param {string} projectId
 * @returns {Promise<ProjectOverview>}
 */
export function getProjectOverview(projectId) {
  return get(`/analytics/projects/${encodeURIComponent(projectId)}/overview`);
}

/**
 * @param {string} projectId
 * @param {'day' | 'hour'} [groupBy]
 * @param {readonly string[]} [excludeAgentIds]
 * @returns {Promise<ProjectTimeseriesBucket[]>}
 */
export function getProjectTimeseries(projectId, groupBy = 'day', excludeAgentIds) {
  const qs = new URLSearchParams({ groupBy });
  if (excludeAgentIds && excludeAgentIds.length > 0) {
    qs.set('excludeAgentIds', excludeAgentIds.join(','));
  }
  return get(`/analytics/projects/${encodeURIComponent(projectId)}/timeseries?${qs}`);
}

/**
 * @param {string} projectId
 * @param {number} [top]
 * @returns {Promise<ProjectAgentBreakdown[]>}
 */
export function getProjectAgents(projectId, top = 20) {
  return get(`/analytics/projects/${encodeURIComponent(projectId)}/agents?top=${top}`);
}

/**
 * @param {string} projectId
 * @returns {Promise<ProjectBudgetStatus>}
 */
export function getProjectBudget(projectId) {
  return get(`/analytics/projects/${encodeURIComponent(projectId)}/budget`);
}
