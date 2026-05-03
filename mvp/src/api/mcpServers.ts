import { get, post, put } from './client'

// Tipos espelham EfsAiHub.Core.Agents.McpServers.McpServer (backend).
// Headers ficam plaintext na coluna Data — secrets sensíveis (Authorization)
// são responsabilidade do operador.

export type RequireApproval = 'never' | 'always'

export interface McpServer {
  id: string
  name: string
  description?: string | null
  serverLabel: string
  serverUrl: string
  allowedTools: string[]
  headers: Record<string, string>
  requireApproval: RequireApproval
  projectId: string
  createdAt: string
  updatedAt: string
}

interface PagedResponse<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

// Body usado tanto em POST quanto em PUT — o backend exige o mesmo shape
// completo (não há PATCH parcial). PUT também exige `id` no body coincidindo
// com o da rota (validado server-side).
export interface SaveMcpServerBody {
  id: string
  name: string
  description?: string | null
  serverLabel: string
  serverUrl: string
  allowedTools: string[]
  headers: Record<string, string>
  requireApproval: RequireApproval
}

const BASE = '/admin/mcp-servers'

// Endpoint paginado — passamos pageSize alto pra carregar tudo de uma vez no
// MVP (volume é baixo).
export const listMcpServers = async (): Promise<McpServer[]> => {
  const page = await get<PagedResponse<McpServer>>(`${BASE}?page=1&pageSize=200`)
  return page.items
}

export const getMcpServer = (id: string) => get<McpServer>(`${BASE}/${id}`)
export const createMcpServer = (body: SaveMcpServerBody) =>
  post<McpServer>(BASE, body)
export const updateMcpServer = (id: string, body: SaveMcpServerBody) =>
  put<McpServer>(`${BASE}/${id}`, body)
