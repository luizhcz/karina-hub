// @ts-check
/**
 * Identidade do PM/PO persistida em localStorage. Substitui
 * mvp/src/stores/identity.ts.
 *
 * - account → header `x-efs-account` (backend lê como userType=cliente)
 * - projectId → header `x-efs-project-id` (scope das chamadas project-scoped)
 * - name + projectName → display visual no header
 *
 * Compatível com a chave 'efs-mvp-identity' usada pelo React, então a
 * sessão persiste entre rotas migradas e não-migradas durante a transição.
 */

import { createStore } from './store.js';

/** @typedef {import('./types.js').Identity} Identity */

const STORAGE_KEY = 'efs-mvp-identity';

/** @type {ReturnType<typeof createStore<Identity | null>>} */
const store = createStore(/** @type {Identity | null} */ (null), STORAGE_KEY);

/** @returns {Identity | null} */
export function getIdentity() {
  const value = store.get();
  if (!value || !value.name || !value.account) return null;
  return {
    name: value.name,
    account: value.account,
    projectId: value.projectId ?? '',
    projectName: value.projectName ?? '',
  };
}

/** @param {Identity} identity */
export function setIdentity(identity) {
  store.set(identity);
}

/** @param {Partial<Identity>} delta */
export function patchIdentity(delta) {
  const current = getIdentity();
  if (!current) return;
  store.set({ ...current, ...delta });
}

export function clearIdentity() {
  store.set(null);
}

/**
 * @param {(id: Identity | null) => void} cb
 * @returns {() => void} unsubscribe
 */
export function subscribeIdentity(cb) {
  return store.subscribe((v) => cb(v));
}
