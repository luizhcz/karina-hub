import { getIdentity } from '../stores/identity'

/** Origem default quando nenhum `app_origin` veio na URL. */
const DEFAULT_APP_ORIGIN = 'web-mvp'

/** Chave em localStorage onde o token bearer do IdP do consumidor é guardado. */
const ACCESS_TOKEN_STORAGE_KEY = 'efs-access-token'

/** Chave em localStorage do `app_origin` capturado da URL no bootstrap. */
const APP_ORIGIN_STORAGE_KEY = 'efs-app-origin'

/**
 * Único ponto onde a app frontend resolve quais headers de identidade enviar
 * pro backend. Devolve:
 *   - `x-efs-account` OU `x-efs-user-profile-id`: identificador do usuário,
 *     populado a partir de `identity.account` quando ela existe (dev local).
 *     Em prod a identity vem do `/me` (proxy injeta `x-efs-*` antes do
 *     backend), então esses headers podem nem ser enviados pelo frontend.
 *   - `x-efs-permissions`: CSV de permissions resolvidas (sincronizadas via /me).
 *   - `x-project-id`: scope de projeto vigente.
 *   - `app_origin`: canal de origem capturado da URL (`bootstrapAuthFromUrl`).
 *     Default `web-mvp` quando ausente.
 *   - `access_token`: bearer opaco do IdP. Capturado da URL no bootstrap
 *     ou setado via `setAccessToken`. Proxy valida e re-emite `x-efs-*`.
 *
 * Importadores: api/client.ts, useChatStream, agentSessions.streamRun. Não
 * importe getIdentity em outros lugares pra compor headers — sempre passe
 * por aqui.
 */
export function getAuthHeaders(): Record<string, string> {
  const id = getIdentity()
  const headers: Record<string, string> = {
    app_origin: readAppOrigin() ?? DEFAULT_APP_ORIGIN,
  }
  if (id?.account) {
    const headerName = id.userType === 'admin' ? 'x-efs-user-profile-id' : 'x-efs-account'
    headers[headerName] = id.account
    headers['x-efs-permissions'] = resolvePermissionsCsv(id.permissions)
  }
  if (id?.projectId) headers['x-project-id'] = id.projectId

  const token = readAccessToken()
  if (token) headers['access_token'] = token

  return headers
}

/**
 * Resolve a CSV de permissions enviada no header. Prioridade:
 *   1. `identity.permissions` (sincronizada via /me).
 *   2. `VITE_DEV_PERMISSIONS` quando rodando em dev sem proxy.
 *   3. String vazia — autenticado sem nenhuma permission (backend aceita).
 */
function resolvePermissionsCsv(permissions: string[] | undefined): string {
  if (permissions && permissions.length > 0) return permissions.join(',')
  const devFallback = import.meta.env.VITE_DEV_PERMISSIONS
  if (typeof devFallback === 'string' && devFallback.trim().length > 0) {
    return devFallback.trim()
  }
  return ''
}

/** Lê o access_token do localStorage. Tolera SSR (window indefinido). */
export function readAccessToken(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)
  } catch {
    return null
  }
}

/**
 * Persiste o access_token no localStorage. Chamado pelo bootstrap quando o
 * token chega via URL, ou no logout (passar null pra remover).
 */
export function setAccessToken(token: string | null): void {
  if (typeof window === 'undefined') return
  try {
    if (token && token.trim().length > 0) {
      window.localStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, token)
    } else {
      window.localStorage.removeItem(ACCESS_TOKEN_STORAGE_KEY)
    }
  } catch {
    // localStorage indisponível (modo privado/Safari) — silencia.
  }
}

/** Lê o app_origin do localStorage. Retorna null se ausente. */
export function readAppOrigin(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(APP_ORIGIN_STORAGE_KEY)
  } catch {
    return null
  }
}

/** Persiste o app_origin no localStorage. Null/empty remove (volta ao default). */
export function setAppOrigin(origin: string | null): void {
  if (typeof window === 'undefined') return
  try {
    if (origin && origin.trim().length > 0) {
      window.localStorage.setItem(APP_ORIGIN_STORAGE_KEY, origin)
    } else {
      window.localStorage.removeItem(APP_ORIGIN_STORAGE_KEY)
    }
  } catch {
    // localStorage indisponível — silencia.
  }
}
