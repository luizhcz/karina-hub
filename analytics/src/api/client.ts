import { getAuthHeaders } from '../auth/headers'
import { API_BASE_URL } from './baseUrl'

const BASE = API_BASE_URL

/**
 * Erro tipado pra que useApi consiga decidir entre retry (5xx) e abort (4xx)
 * sem string-matching de message. Todo handler de erro na UI deve checar
 * `err instanceof ApiError` antes de exibir mensagem.
 */
export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

async function extractError(res: Response): Promise<string> {
  try {
    const body = (await res.clone().json()) as { error?: unknown; message?: unknown }
    if (typeof body?.error === 'string') return body.error
    if (typeof body?.message === 'string') return body.message
  } catch {
    // body sem JSON — usa fallback.
  }
  return `HTTP ${res.status}`
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const merged: Record<string, string> = {
    ...getAuthHeaders(),
    ...(init?.headers as Record<string, string> | undefined),
  }
  const res = await fetch(`${BASE}${path}`, { ...init, headers: merged })
  if (!res.ok) {
    throw new ApiError(res.status, await extractError(res))
  }
  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

export function get<T>(path: string, init?: RequestInit): Promise<T> {
  return request<T>(path, init)
}

export function post<T>(path: string, body: unknown, extraHeaders?: Record<string, string>): Promise<T> {
  return request<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...extraHeaders },
    body: JSON.stringify(body),
  })
}

export function put<T>(path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function patch<T>(path: string, body: unknown): Promise<T> {
  return request<T>(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
}

export function del<T>(path: string): Promise<T> {
  return request<T>(path, { method: 'DELETE' })
}

/**
 * Converte erro em mensagem amigável pra UI. Filtra mensagens internas do
 * backend (gating de admin, projeto default) que não fazem sentido pro
 * público-alvo (PM/PO).
 */
export function friendlyError(err: unknown, fallback = 'Algo deu errado. Tente novamente.'): string {
  if (err instanceof ApiError) {
    if (err.status === 403) return 'Você não tem acesso a este recurso.'
    if (err.status === 404) return 'Não encontramos o que você procura.'
    if (err.status === 429) return 'Muitas requisições — aguarde um instante.'
    if (err.status >= 500) return 'O servidor está com problemas. Tente novamente em instantes.'
    return err.message || fallback
  }
  if (err instanceof Error) return err.message || fallback
  return fallback
}
