import { getIdentity } from '../stores/identity'

/** Origem do app — identifica este MVP nas chamadas downstream (generic tools). */
const APP_ORIGIN = 'web-mvp'

/** Chave em localStorage onde o token bearer do IdP do consumidor é guardado. */
const ACCESS_TOKEN_STORAGE_KEY = 'efs-access-token'

/**
 * Único ponto onde a app frontend resolve quais headers de identidade enviar
 * pro backend. Devolve:
 *   - `x-efs-account` OU `x-efs-user-profile-id`: identificador do usuário.
 *     A escolha depende de `identity.userType` (cliente → account, admin →
 *     profile id). Backend usa o header pra classificar a origem (`UserType`
 *     do user) — afeta ChatRouting default, persona schema (ClientPersona vs
 *     AdminPersona) e templates de prompt. Gating admin (IsAdmin) é
 *     independente e vem do DB.
 *   - `x-project-id`: scope de projeto vigente.
 *   - `app_origin`: canal de origem (`web-mvp`) — propagado pro downstream.
 *   - `access_token`: bearer opaco do IdP do consumidor (quando presente).
 *     Backend usa pra forwardar nas chamadas de generic tools — 401 do
 *     downstream = caller sem permissão na ferramenta.
 *
 * Quando o login migrar pra access_token validado server-side, basta trocar
 * essa função (ou registrar um provider plugável aqui) — nenhum outro arquivo
 * lê os headers diretamente.
 *
 * Importadores: api/client.ts, useChatStream, agentSessions.streamRun. Não
 * importe getIdentity em outros lugares pra compor headers — sempre passe
 * por aqui.
 */
export function getAuthHeaders(): Record<string, string> {
  const id = getIdentity()
  const headers: Record<string, string> = {
    app_origin: APP_ORIGIN,
  }
  if (id?.account) {
    const headerName = id.userType === 'admin' ? 'x-efs-user-profile-id' : 'x-efs-account'
    headers[headerName] = id.account
  }
  if (id?.projectId) headers['x-project-id'] = id.projectId

  const token = readAccessToken()
  if (token) headers['access_token'] = token

  return headers
}

/** Lê o access_token do localStorage. Tolera SSR (window indefinido). */
function readAccessToken(): string | null {
  if (typeof window === 'undefined') return null
  try {
    return window.localStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)
  } catch {
    return null
  }
}

/**
 * Persiste o access_token no localStorage. Chamado quando o frontend
 * receber/atualizar o token (ex.: callback de SSO). Passar null/empty
 * remove.
 */
export function setAccessToken(token: string | null) {
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
