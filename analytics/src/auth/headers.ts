import { getIdentity } from '../stores/identity'

/** Origem default quando nenhum `app_origin` veio na URL. */
const DEFAULT_APP_ORIGIN = 'web-analytics'

/** Chave em localStorage onde o token bearer do IdP do consumidor é guardado. */
const ACCESS_TOKEN_STORAGE_KEY = 'efs-access-token'

/** Chave em localStorage do `app_origin` capturado da URL no bootstrap. */
const APP_ORIGIN_STORAGE_KEY = 'efs-app-origin'

/**
 * Único ponto onde o frontend resolve quais headers de identidade enviar pro
 * backend. Devolve:
 *   - `x-efs-account` OU `x-efs-user-profile-id`: identificador do usuário.
 *     Em prod, o proxy injeta esses headers antes do backend — em dev local,
 *     o frontend envia direto a partir do identity.
 *   - `x-efs-permissions`: CSV de permissions sincronizada via /me.
 *   - `x-project-id`: scope de projeto vigente.
 *   - `x-tenant-id`: tenant ecoado pelo /me (quando ausente cai em "default").
 *   - `app_origin`: canal de origem capturado da URL (bootstrap).
 *   - `access_token`: bearer opaco do IdP, proxy valida e re-emite `x-efs-*`.
 *
 * Importadores: api/client.ts. Não importe getIdentity em outros lugares pra
 * compor headers — sempre passe por aqui.
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
  if (id?.tenantId) headers['x-tenant-id'] = id.tenantId

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

export function readAccessToken(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)
  } catch {
    return null
  }
}

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

export function readAppOrigin(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(APP_ORIGIN_STORAGE_KEY)
  } catch {
    return null
  }
}

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
