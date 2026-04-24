import { get, post, put, del } from './client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'


/**
 * Servidor MCP registrado em aihub.mcp_servers. Agents referenciam por Id;
 * o AzureFoundryClientProvider resolve serverLabel/serverUrl/allowedTools/headers
 * em runtime a partir desse registro (live — mudanças propagam automaticamente).
 */
export interface McpServer {
  id: string
  name: string
  description?: string
  serverLabel: string
  serverUrl: string
  allowedTools: string[]
  headers: Record<string, string>
  requireApproval: 'never' | 'always'
  projectId: string
  createdAt: string
  updatedAt: string
}

export type CreateMcpServerRequest = Omit<McpServer, 'projectId' | 'createdAt' | 'updatedAt'>

interface McpServerPageResponse {
  items: McpServer[]
  total: number
  page: number
  pageSize: number
}


export const KEYS = {
  all: ['mcp-servers'] as const,
  detail: (id: string) => ['mcp-servers', id] as const,
}


// Backend retorna paginado — extrai .items pra manter interface de array nos callers
// (pattern do fix em pricing.ts/skills.ts/modelCatalog.ts pós-commit 3016a8e).
export const getMcpServers = async (): Promise<McpServer[]> => {
  const page = await get<McpServerPageResponse>('/admin/mcp-servers', { pageSize: 200 })
  return page.items
}

export const getMcpServer = (id: string) => get<McpServer>(`/admin/mcp-servers/${id}`)

export const createMcpServer = (body: CreateMcpServerRequest) =>
  post<McpServer>('/admin/mcp-servers', body)

export const updateMcpServer = (id: string, body: CreateMcpServerRequest) =>
  put<McpServer>(`/admin/mcp-servers/${id}`, body)

export const deleteMcpServer = (id: string) => del(`/admin/mcp-servers/${id}`)


export function useMcpServers() {
  return useQuery({ queryKey: KEYS.all, queryFn: getMcpServers })
}

export function useMcpServer(id: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.detail(id),
    queryFn: () => getMcpServer(id),
    enabled: !!id && enabled,
  })
}

export function useCreateMcpServer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createMcpServer,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useUpdateMcpServer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateMcpServerRequest }) => updateMcpServer(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteMcpServer() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteMcpServer,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}
