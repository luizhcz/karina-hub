import { getIdentity } from '../stores/identity'

/**
 * Único ponto onde a app frontend resolve quais headers de identidade enviar
 * pro backend. Hoje devolve `x-efs-account` + `x-project-id`. Quando o login
 * migrar pra access_token, basta trocar essa função (ou registrar um
 * provider plugável aqui) — nenhum outro arquivo lê os headers diretamente.
 *
 * Importadores: api/client.ts. Não importe getIdentity em outros lugares pra
 * compor headers — sempre passe por aqui.
 */
export function getAuthHeaders(): Record<string, string> {
  const id = getIdentity()
  const headers: Record<string, string> = {}
  if (id?.account) headers['x-efs-account'] = id.account
  if (id?.projectId) headers['x-project-id'] = id.projectId
  return headers
}
