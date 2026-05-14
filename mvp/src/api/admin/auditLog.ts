import { get } from '../client'

export interface AdminAuditEntry {
  id: number
  tenantId: string | null
  projectId: string | null
  actorUserId: string
  actorUserType: string | null
  action: string
  resourceType: string
  resourceId: string
  payloadBefore: unknown | null
  payloadAfter: unknown | null
  timestamp: string
}

export interface AdminAuditListResponse {
  items: AdminAuditEntry[]
  total: number
  page: number
  pageSize: number
}

export interface ListAuditLogParams {
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

export function listAuditLog(params: ListAuditLogParams = {}): Promise<AdminAuditListResponse> {
  const qs = new URLSearchParams()
  if (params.projectId) qs.set('projectId', params.projectId)
  if (params.resourceType) qs.set('resourceType', params.resourceType)
  if (params.resourceId) qs.set('resourceId', params.resourceId)
  if (params.actorUserId) qs.set('actorUserId', params.actorUserId)
  if (params.action) qs.set('action', params.action)
  if (params.from) qs.set('from', params.from)
  if (params.to) qs.set('to', params.to)
  if (params.page) qs.set('page', String(params.page))
  if (params.pageSize) qs.set('pageSize', String(params.pageSize))
  const suffix = qs.toString() ? `?${qs}` : ''
  return get<AdminAuditListResponse>(`/admin/audit-log${suffix}`)
}
