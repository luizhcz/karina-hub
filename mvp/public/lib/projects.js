// @ts-check
/**
 * API helpers pra `aihub.projects`. Substitui mvp/src/api/projects.ts.
 */

import { get } from './api.js';

/** @typedef {import('./types.js').Project} Project */

/** @returns {Promise<Project[]>} */
export function listProjects() {
  return get('/projects');
}
