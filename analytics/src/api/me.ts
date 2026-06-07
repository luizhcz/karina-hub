import { get } from './client'

export interface ProjectRef {
  id: string
  name: string
}

// Identidade resolvida do caller + flag de admin + permissions ecoadas do
// header + lista de projetos visíveis. Endpoint público (200 sempre).
// `projects=[]` em non-admin sinaliza "sem nenhum vínculo".
// Espelha src/EfsAiHub.Host.Api/Controllers/MeController.cs:39-91.
export interface MeResponse {
  accountId: string | null
  userType: 'cliente' | 'admin' | null
  isAdmin: boolean
  permissions: string[]
  userId?: string | null
  displayName?: string | null
  /**
   * Tenant resolvido pelo backend a partir de `x-tenant-id` (ou `default` quando
   * ausente). Frontend persiste em identity.tenantId pra re-enviar o header em
   * chamadas subsequentes.
   */
  tenantId?: string | null
  projects: ProjectRef[]
}

export const getMe = () => get<MeResponse>('/me')
