import { get } from './client'

export interface Project {
  id: string
  name: string
  tenantId: string
  description?: string | null
  createdAt: string
  updatedAt: string
  /**
   * Permite ao projeto criar implantações tipo Chat (workflow Graph com
   * Router conversacional + branches Conversational + InputMode=Chat).
   * Default `false` no backend; só projetos seedados com flag `true`
   * (ex: `sales-trader-ai`) podem criar deploys do tipo. Frontend usa
   * pra filtrar/desabilitar o card "Chat" no modal de nova implantação.
   */
  chatDeploymentAllowed: boolean
}

export function listProjects(): Promise<Project[]> {
  return get<Project[]>('/projects')
}
