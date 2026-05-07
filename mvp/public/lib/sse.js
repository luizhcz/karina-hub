// @ts-check
/**
 * Wrapper sobre EventSource. EventSource é nativo do browser, faz reconnect
 * automático sob status != CLOSED. Substitui o async-generator streamRun()
 * de mvp/src/api/agentSessions.ts e o consumer de EvalStatusCard.
 *
 * EventSource não envia headers customizados → identidade vai por query
 * param (?account=X&projectId=Y) em endpoints liberados pra esse fallback
 * (agente sessions, evaluations). Função buildSseUrl() injeta isso.
 */

import { getIdentity } from './identity.js';

/**
 * Concatena query params de identidade pra URLs SSE.
 *
 * @param {string} path Rota no formato /api/aihub/...
 * @param {Record<string, string>} [extra] Params adicionais
 * @returns {string} URL absoluta com query string
 */
export function buildSseUrl(path, extra) {
  const id = getIdentity();
  /** @type {Record<string, string>} */
  const params = { ...(extra ?? {}) };
  if (id?.account) params.account = id.account;
  if (id?.projectId) params.projectId = id.projectId;
  const qs = new URLSearchParams(params).toString();
  return `${path}${qs ? `?${qs}` : ''}`;
}

/**
 * @typedef {object} StreamHandlers
 * @property {(event: MessageEvent) => void} [onEvent] Mensagem genérica (event sem nome)
 * @property {(event: Event) => void} [onError] EventSource em erro
 * @property {Record<string, (event: MessageEvent) => void>} [namedEvents] Listeners por event name (SSE event:)
 */

/**
 * Abre uma conexão SSE. Retorna função pra fechar.
 *
 * @param {string} url URL completa (use buildSseUrl pra injetar identidade)
 * @param {StreamHandlers} handlers
 * @returns {() => void} cancel function
 */
export function streamSse(url, handlers) {
  const es = new EventSource(url);

  if (handlers.onEvent) {
    es.onmessage = handlers.onEvent;
  }
  if (handlers.onError) {
    es.onerror = handlers.onError;
  }
  if (handlers.namedEvents) {
    for (const [name, cb] of Object.entries(handlers.namedEvents)) {
      es.addEventListener(name, /** @type {EventListener} */ (cb));
    }
  }

  return () => {
    es.close();
  };
}
