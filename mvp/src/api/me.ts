import { get } from './client'

// Identidade resolvida do caller + flag de admin. Endpoint público —
// sempre retorna 200, sem barulho de 403 no console do navegador.
export interface MeResponse {
  accountId: string | null
  userType: 'cliente' | 'admin' | null
  isAdmin: boolean
}

export const getMe = () => get<MeResponse>('/me')
