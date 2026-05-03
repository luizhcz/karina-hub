import { get, post, put } from './client'

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
  whenToUse: string | null
  createdAt: string
  updatedAt: string
}

export interface CreateGenericToolBody {
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
  whenToUse: string | null
}

export interface UpdateGenericToolBody extends Omit<CreateGenericToolBody, 'id'> {
  expectedUpdatedAt: string
}

export const listGenericTools = () => get<GenericTool[]>('/generic-tools')
export const getGenericTool = (id: string) => get<GenericTool>(`/generic-tools/${id}`)
export const createGenericTool = (body: CreateGenericToolBody) =>
  post<GenericTool>('/generic-tools', body)
export const updateGenericTool = (id: string, body: UpdateGenericToolBody) =>
  put<GenericTool>(`/generic-tools/${id}`, body)

// Detecta placeholders {nome} no UrlTemplate em ordem de aparição. Usado pra
// auto-popular path params no editor.
export function extractPlaceholders(urlTemplate: string): string[] {
  const regex = /\{([A-Za-z_][A-Za-z0-9_]*)\}/g
  const found = new Set<string>()
  let match: RegExpExecArray | null
  while ((match = regex.exec(urlTemplate)) !== null) {
    found.add(match[1])
  }
  return Array.from(found)
}
