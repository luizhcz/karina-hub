// @ts-check
/**
 * API helpers do system endpoint. Substitui mvp/src/api/system.ts.
 */

import { get } from './api.js';

/**
 * @typedef {object} SystemInfo
 * @property {string} publicBaseUrl URL pública do backend (mostrada nos exemplos de consumo)
 * @property {string} [version]
 */

/** @returns {Promise<SystemInfo>} */
export function getSystemInfo() {
  return get('/system/info');
}
