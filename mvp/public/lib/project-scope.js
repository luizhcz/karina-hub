// @ts-check
/**
 * Filtro de UX: o backend retorna agentes/workflows com Visibility=global
 * cross-project (HasQueryFilter), mas no frontend o usuário só deve ver
 * recursos do próprio projeto. Visibility continua válido pra runtime/consumo —
 * é só separação visual.
 *
 * Substitui mvp/src/stores/projectScope.ts.
 */

import { getIdentity } from './identity.js';

/**
 * @param {{ projectId?: string | null, originProjectId?: string | null }} item
 * @returns {boolean}
 */
export function isInCurrentProject(item) {
  const currentId = getIdentity()?.projectId;
  if (!currentId) return true;
  const owner = item.originProjectId ?? item.projectId ?? null;
  if (!owner) return false;
  return owner === currentId;
}
