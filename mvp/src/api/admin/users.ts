import { get, patch, put } from '../client'

export interface AdminUserSummary {
  id: string
  externalUserId: string
  userType: 'cliente' | 'admin'
  displayName: string
  isAdmin: boolean
  createdAt: string
  lastSeenAt: string
  projectCount: number
}

export interface AdminUserDetail {
  id: string
  externalUserId: string
  userType: 'cliente' | 'admin'
  displayName: string
  isAdmin: boolean
  createdAt: string
  lastSeenAt: string
  projectIds: string[]
}

export interface AdminUsersListResponse {
  page: number
  pageSize: number
  total: number
  items: AdminUserSummary[]
}

export interface ListAdminUsersParams {
  search?: string
  page?: number
  pageSize?: number
}

export function listAdminUsers(params: ListAdminUsersParams = {}): Promise<AdminUsersListResponse> {
  const qs = new URLSearchParams()
  if (params.search) qs.set('search', params.search)
  if (params.page) qs.set('page', String(params.page))
  if (params.pageSize) qs.set('pageSize', String(params.pageSize))
  const suffix = qs.toString() ? `?${qs}` : ''
  return get<AdminUsersListResponse>(`/admin/users${suffix}`)
}

export function getAdminUser(id: string): Promise<AdminUserDetail> {
  return get<AdminUserDetail>(`/admin/users/${id}`)
}

export function setUserProjects(id: string, projectIds: string[]): Promise<AdminUserDetail> {
  return put<AdminUserDetail>(`/admin/users/${id}/projects`, { projectIds })
}

export function patchAdminUser(
  id: string,
  body: { isAdmin?: boolean; displayName?: string },
): Promise<AdminUserDetail> {
  return patch<AdminUserDetail>(`/admin/users/${id}`, body)
}

