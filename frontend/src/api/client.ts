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

/**
 * Erro padronizado de chamada à API do backend.
 *
 * O backend retorna payload JSON `{ error: "mensagem" }` em erros conhecidos:
 * - 400 DomainException (invariante de domínio violada) — mensagem técnica da regra
 * - 402 BudgetExceededException (teto diário atingido)
 * - 403 DefaultProjectGuard ou permissão
 * - 404 recurso não encontrado (ou HITL CAS perdido em `/resolve`)
 * - 409 conflict (ex: versionamento otimista)
 * - 429 rate limit por projeto
 *
 * Em qualquer outro status ou body malformado, `message` vira `HTTP {status}`.
 */
export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/**
 * Status codes em que o backend emite payload `{ error: string }` pelo
 * `GlobalExceptionMiddleware` ou handlers equivalentes. Safe para tentar `res.clone().json()`.
 */
const STATUS_WITH_ERROR_BODY: ReadonlySet<number> = new Set([400, 402, 403, 404, 409, 429])

/**
 * Tenta extrair `{ error: string }` do body da Response. Nunca throws — retorna o fallback
 * se o body não for JSON válido ou se o campo não existir.
 */
async function extractErrorMessage(res: Response, fallback: string): Promise<string> {
  try {
    const body = (await res.clone().json()) as { error?: unknown; message?: unknown } | null
    if (body && typeof body === 'object') {
      if (typeof body.error === 'string' && body.error.length > 0) return body.error
      if (typeof body.message === 'string' && body.message.length > 0) return body.message
    }
    return fallback
  } catch {
    return fallback
  }
}

/**
 * Wrapper universal sobre `fetch` que:
 * - injeta headers de identidade (tenant/project/account) via `getIdentityHeaders()`.
 * - converte statuses de erro conhecidos em `ApiError` com mensagem legível do backend
 *   (extraída de `{ error }` ou `{ message }` no body JSON).
 * - retorna 204 como `undefined`.
 * - retorna JSON parseado para 2xx.
 *
 * Callers de mutation (TanStack Query) recebem `ApiError.message` já com a mensagem real
 * do backend, prontas para exibir em toast sem tradução adicional.
 */
export async function safeFetch<T>(url: string, init?: RequestInit): Promise<T> {
  const identityHeaders = getIdentityHeaders()
  const merged: Record<string, string> = {
    ...identityHeaders,
    ...(init?.headers as Record<string, string> | undefined),
  }
  const res = await fetch(url, { ...init, headers: merged })
  if (!res.ok) {
    const fallback = `HTTP ${res.status}`
    const message = STATUS_WITH_ERROR_BODY.has(res.status)
      ? await extractErrorMessage(res, fallback)
      : fallback
    throw new ApiError(res.status, message)
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
