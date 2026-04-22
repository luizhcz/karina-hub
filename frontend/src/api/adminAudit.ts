import { get } from './client'
import { useQuery } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

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
  action: 'create' | 'update' | 'delete'
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

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  audit: (params?: AdminAuditParams) => ['admin', 'audit-log', params] as const,
}

// ── Raw API ──────────────────────────────────────────────────────────────────

export const getAdminAudit = (params?: AdminAuditParams) =>
  get<AdminAuditPage>('/admin/audit-log', params as Record<string, string | number | undefined>)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useAdminAudit(params?: AdminAuditParams) {
  return useQuery({
    queryKey: KEYS.audit(params),
    queryFn: () => getAdminAudit(params),
  })
}
