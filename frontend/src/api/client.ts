import { useUserStore } from '../stores/user'
import { useProjectStore } from '../stores/project'

const BASE = '/api'

export function getIdentityHeaders(): Record<string, string> {
  const { userId, userType } = useUserStore.getState()
  const { projectId } = useProjectStore.getState()
  const headers: Record<string, string> = {}
  if (userId) {
    if (userType === 'cliente') headers['x-efs-account'] = userId
    else headers['x-efs-user-profile-id'] = userId
  }
  if (projectId && projectId !== 'default') {
    headers['x-efs-project-id'] = projectId
  }
  return headers
}

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export async function safeFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const identityHeaders = getIdentityHeaders()
  const merged: Record<string, string> = {
    ...identityHeaders,
    ...(init?.headers as Record<string, string> | undefined),
  }
  const res = await fetch(url, { ...init, headers: merged })
  if (!res.ok) {
    if (res.status === 403) {
      try {
        const body = await res.clone().json() as { error?: string }
        throw new ApiError(403, body.error ?? 'Acesso negado')
      } catch (e) {
        if (e instanceof ApiError) throw e
      }
    }
    throw new ApiError(res.status, `HTTP ${res.status}`)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export function apiUrl(path: string): string {
  return `${BASE}${path}`
}

function buildQuery(params: object): string {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(params)) {
    if (v !== undefined && v !== '') q.set(k, String(v))
  }
  const s = q.toString()
  return s ? `?${s}` : ''
}

export function get<T>(path: string, params?: object): Promise<T> {
  return safeFetch<T>(`${BASE}${path}${params ? buildQuery(params) : ''}`)
}

export function post<T>(path: string, body?: unknown): Promise<T> {
  return safeFetch<T>(`${BASE}${path}`, {
    method: 'POST',
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  })
}

export function put<T>(path: string, body: unknown): Promise<T> {
  return safeFetch<T>(`${BASE}${path}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function del(path: string): Promise<void> {
  return safeFetch<void>(`${BASE}${path}`, { method: 'DELETE' })
}

export function withHeaders(headers: Record<string, string>) {
  return {
    get: <T>(path: string) => safeFetch<T>(`${BASE}${path}`, { headers }),
    post: <T>(path: string, body?: unknown) =>
      safeFetch<T>(`${BASE}${path}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...headers },
        body: body ? JSON.stringify(body) : undefined,
      }),
  }
}
