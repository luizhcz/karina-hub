import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

export type HttpMethodType = 'GET' | 'POST'
export type InputContentType = 'None' | 'Json' | 'Text' | 'FormUrlEncoded'
export type OutputContentType = 'Json' | 'Text' | 'Csv'

export interface ParamDefinition {
  type: string
  description: string
  required: boolean
}

export interface GenericTool {
  id: string
  projectId: string
  tenantId: string
  name: string
  description: string
  httpMethod: HttpMethodType
  urlTemplate: string
  pathParams: Record<string, ParamDefinition>
  queryParams: Record<string, ParamDefinition>
  customHeaders: Record<string, string>
  inputContentType: InputContentType
  inputSchema: string | null
  outputContentType: OutputContentType
  outputSchema: string | null
  timeoutSecondsOverride: number | null
  createdAt: string
  updatedAt: string
}

export interface CreateGenericToolRequest {
  id?: string
  name: string
  description: string
  httpMethod: HttpMethodType
  urlTemplate: string
  pathParams: Record<string, ParamDefinition>
  queryParams: Record<string, ParamDefinition>
  customHeaders: Record<string, string>
  inputContentType: InputContentType
  inputSchema: string | null
  outputContentType: OutputContentType
  outputSchema: string | null
  timeoutSecondsOverride: number | null
}

export interface UpdateGenericToolRequest extends Omit<CreateGenericToolRequest, 'id'> {
  expectedUpdatedAt: string
}

export const KEYS = {
  all: ['generic-tools'] as const,
  detail: (id: string) => ['generic-tools', id] as const,
}

export const getGenericTools = () => get<GenericTool[]>('/generic-tools')
export const getGenericTool = (id: string) => get<GenericTool>(`/generic-tools/${id}`)
export const createGenericTool = (body: CreateGenericToolRequest) =>
  post<GenericTool>('/generic-tools', body)
export const updateGenericTool = (id: string, body: UpdateGenericToolRequest) =>
  put<GenericTool>(`/generic-tools/${id}`, body)
export const deleteGenericTool = (id: string) => del(`/generic-tools/${id}`)

export function useGenericTools() {
  return useQuery({ queryKey: KEYS.all, queryFn: getGenericTools })
}

export function useGenericTool(id: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.detail(id),
    queryFn: () => getGenericTool(id),
    enabled,
  })
}

export function useCreateGenericTool() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createGenericTool,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: KEYS.all })
    },
  })
}

export function useUpdateGenericTool() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateGenericToolRequest }) =>
      updateGenericTool(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteGenericTool() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteGenericTool,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: KEYS.all })
    },
  })
}

/**
 * Extrai placeholders {x} do urlTemplate em ordem de aparição.
 * Usado pra auto-popular path params no formulário.
 */
export function extractPlaceholders(urlTemplate: string): string[] {
  const regex = /\{([A-Za-z_][A-Za-z0-9_]*)\}/g
  const found = new Set<string>()
  let match: RegExpExecArray | null
  while ((match = regex.exec(urlTemplate)) !== null) {
    found.add(match[1])
  }
  return Array.from(found)
}
