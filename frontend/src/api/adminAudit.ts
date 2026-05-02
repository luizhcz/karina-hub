import { get } from './client'
import { useQuery } from '@tanstack/react-query'


/**
 * Entrada da trilha /api/admin/audit-log — espelha AdminAuditEntry do backend.
 * `payloadBefore` e `payloadAfter` são JsonElement opacos (já que dependem do
 * resourceType); o consumidor inspeciona via JsonViewer no detalhe da linha.
 */
export interface AdminAuditEntry {
  id: number
  tenantId?: string
  projectId?: string
  actorUserId: string
  actorUserType?: string
  /**
   * Tipos canônicos: create | update | delete + actions especializadas como
   * agent.visibility_changed, workflow.agent_version_pinned, etc. Mantido como
   * string aberto pra acomodar evolução do AdminAuditActions sem coupling.
   */
  action: string
  resourceType: 'project' | 'agent' | 'workflow' | 'skill' | 'model_pricing'
  resourceId: string
  payloadBefore?: unknown
  payloadAfter?: unknown
  timestamp: string
}

export interface AdminAuditPage {
  items: AdminAuditEntry[]
  total: number
  page: number
  pageSize: number
}

export interface AdminAuditParams {
  projectId?: string
  resourceType?: string
  resourceId?: string
  actorUserId?: string
  action?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}


export const KEYS = {
  audit: (params?: AdminAuditParams) => ['admin', 'audit-log', params] as const,
}


export const getAdminAudit = (params?: AdminAuditParams) =>
  get<AdminAuditPage>('/admin/audit-log', params as Record<string, string | number | undefined>)


export function useAdminAudit(params?: AdminAuditParams) {
  return useQuery({
    queryKey: KEYS.audit(params),
    queryFn: () => getAdminAudit(params),
  })
}
