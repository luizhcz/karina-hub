// @ts-check
/**
 * Catálogo de modelos pré-definidos (presets curados pelo time de governança).
 * Endpoint público — backend filtra Enabled=true server-side.
 *
 * Substitui mvp/src/api/predefinedModels.ts.
 */

import { get } from './api.js';

/**
 * @typedef {object} PredefinedModel
 * @property {string} id
 * @property {string} displayName
 * @property {string} description
 * @property {string} provider
 * @property {string | null} [clientType]
 * @property {string | null} [endpoint]
 * @property {string} deploymentName
 * @property {number | null} [defaultTemperature]
 * @property {number | null} [defaultMaxTokens]
 * @property {boolean} enabled
 * @property {string} createdAt
 * @property {string} updatedAt
 */

/** @returns {Promise<PredefinedModel[]>} */
export function listPredefinedModels() {
  return get('/predefined-models');
}
