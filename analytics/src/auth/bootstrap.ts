import { setAccessToken, setAppOrigin } from './headers'

/**
 * Captura `access_token` e `app_origin` da URL na inicialização do app,
 * persiste em localStorage e limpa da query string via `history.replaceState`.
 * Evita que o token vaze em logs, screenshots ou rotas restauradas pelo
 * `BrowserRouter`.
 *
 * Cenário: usuário acessa o analytics com
 *   https://analytics/?access_token=<bearer>&app_origin=<canal>
 * vindos do IdP/proxy corporativo. Frontend persiste os 2 e passa a enviá-los
 * como headers em toda chamada via `getAuthHeaders`.
 */
export function bootstrapAuthFromUrl(): void {
  if (typeof window === 'undefined') return

  const url = new URL(window.location.href)
  const token = url.searchParams.get('access_token')
  const origin = url.searchParams.get('app_origin')

  if (!token && !origin) return

  if (token && token.trim().length > 0) setAccessToken(token.trim())
  if (origin && origin.trim().length > 0) setAppOrigin(origin.trim())

  url.searchParams.delete('access_token')
  url.searchParams.delete('app_origin')
  const cleaned = `${url.pathname}${url.search ? url.search : ''}${url.hash}`
  window.history.replaceState(window.history.state, '', cleaned)
}
