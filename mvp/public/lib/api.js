// @ts-check
/**
 * Cliente HTTP do backend. Injeta x-efs-account e x-efs-project-id baseado
 * em getIdentity() do store, normaliza erros via ApiError + friendlyError.
 *
 * Substitui mvp/src/api/client.ts.
 */

import { getIdentity } from './identity.js';

const BASE = '/api/aihub';

export class ApiError extends Error {
  /**
   * @param {number} status
   * @param {string} message
   */
  constructor(status, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

/** @returns {Record<string, string>} */
function identityHeaders() {
  const id = getIdentity();
  /** @type {Record<string, string>} */
  const headers = {};
  if (id?.account) headers['x-efs-account'] = id.account;
  if (id?.projectId) headers['x-efs-project-id'] = id.projectId;
  return headers;
}

/**
 * @param {Response} res
 * @returns {Promise<string>}
 */
async function extractError(res) {
  try {
    const body = await res.clone().json();
    if (typeof body?.error === 'string') return body.error;
    if (typeof body?.message === 'string') return body.message;
  } catch {
    // body sem JSON — usa fallback.
  }
  return `HTTP ${res.status}`;
}

/**
 * @template T
 * @param {string} path
 * @param {RequestInit} [init]
 * @returns {Promise<T>}
 */
async function request(path, init) {
  /** @type {Record<string, string>} */
  const merged = {
    ...identityHeaders(),
    .../** @type {Record<string, string>} */ (init?.headers ?? {}),
  };
  const res = await fetch(`${BASE}${path}`, { ...init, headers: merged });
  if (!res.ok) {
    throw new ApiError(res.status, await extractError(res));
  }
  if (res.status === 204) return /** @type {T} */ (/** @type {unknown} */ (undefined));
  return /** @type {T} */ (await res.json());
}

/**
 * @template T
 * @param {string} path
 * @returns {Promise<T>}
 */
export function get(path) {
  return request(path);
}

/**
 * @template T
 * @param {string} path
 * @param {unknown} body
 * @returns {Promise<T>}
 */
export function post(path, body) {
  return request(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * @template T
 * @param {string} path
 * @param {unknown} body
 * @returns {Promise<T>}
 */
export function put(path, body) {
  return request(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * @template T
 * @param {string} path
 * @param {unknown} body
 * @returns {Promise<T>}
 */
export function patch(path, body) {
  return request(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/**
 * @template T
 * @param {string} path
 * @returns {Promise<T>}
 */
export function del(path) {
  return request(path, { method: 'DELETE' });
}

// Marcadores de mensagens internas do backend que NÃO devem aparecer pra
// PMs/POs. Filtramos pra evitar vazar detalhes do gating administrativo.
const TECHNICAL_MARKERS = [
  "projeto 'default'",
  'projeto "default"',
  'permissão de administrador',
  'permissao de administrador',
  'admin gate',
  'requer permissão de admin',
];

/** @param {string} message */
function isTechnicalLeak(message) {
  const lower = message.toLowerCase();
  return TECHNICAL_MARKERS.some((marker) => lower.includes(marker.toLowerCase()));
}

/**
 * Converte erro em mensagem amigável pra UI. Filtra mensagens técnicas
 * do backend (gating de admin, projeto default) que não fazem sentido pro
 * público-alvo do MVP.
 *
 * @param {unknown} err
 * @param {string} [fallback]
 * @returns {string}
 */
export function friendlyError(err, fallback = 'Algo deu errado. Tente novamente.') {
  if (err instanceof ApiError) {
    if (err.status === 403 || isTechnicalLeak(err.message)) {
      return 'Você não tem acesso a este recurso.';
    }
    if (err.status === 404) return 'Não encontramos o que você procura.';
    if (err.status === 409) return err.message || 'Conflito ao salvar.';
    if (err.status === 412) return 'Esse item foi atualizado em paralelo. Recarregue e tente novamente.';
    if (err.status === 429) return 'Muitas requisições — aguarde um instante.';
    if (err.status >= 500) return 'O servidor está com problemas. Tente novamente em instantes.';
    return err.message || fallback;
  }
  return fallback;
}
