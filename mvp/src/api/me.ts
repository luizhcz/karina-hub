import { get } from './client'

export interface ProjectRef {
  id: string
  name: string
}

// Identidade resolvida do caller + flag de admin + permissions ecoadas do
// header + lista de projetos visíveis. Endpoint público — sempre retorna 200,
// sem barulho de 403 no console. `projects=[]` em non-admin sinaliza "sem
// nenhum vínculo" — frontend redireciona pra /bem-vindo nesse caso.
export interface MeResponse {
  accountId: string | null
  userType: 'cliente' | 'admin' | null
  isAdmin: boolean
  permissions: string[]
  userId?: string | null
  displayName?: string | null
  /**
   * Tenant resolvido pelo backend a partir do header `x-tenant-id` (ou
   * `default` quando ausente). Pass-through puro — backend não valida contra
   * catálogo. Frontend persiste em `identity.tenantId` pra re-enviar o header
   * em chamadas subsequentes (ver getAuthHeaders).
   */
  tenantId?: string | null
  projects: ProjectRef[]
}

export const getMe = () => get<MeResponse>('/me')
