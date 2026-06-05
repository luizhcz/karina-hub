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
  /**
   * Quando true, é "exclusiva do usuário": backend forwarda app_origin e
   * access_token da request original na chamada downstream. Tool geral
   * (false, default) não recebe esses headers.
   */
  isExclusive: boolean
  /**
   * Modo de projeção do response contra `outputSchema`. Setado automaticamente:
   * `'Project'` para Json/Csv (drop silencioso de extras + fail-loud em
   * required/type), `'Off'` apenas para Text (sem shape pra projetar).
   */
  outputProjectionMode: OutputProjectionMode
  /**
   * Avisos emitidos quando os schemas foram canonicalizados no save. Backend
   * popula em Create/Update — null no GET/List. UI mostra banner pro autor
   * saber o que foi transformado (ex.: oneOf colapsado em superset).
   */
  schemaWarnings?: SchemaWarning[] | null
  createdAt: string
  updatedAt: string
}

export type OutputProjectionMode = 'Off' | 'Project'

export interface SchemaWarning {
  code: string
  path: string
  message: string
}

export interface CreateGenericToolBody {
  id?: string
  name: string
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
  isExclusive: boolean
  outputProjectionMode: OutputProjectionMode
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

// Envelope verboso retornado por POST /{id}/execute. Não usar em runtime de
// agente — esse endpoint só serve pra teste manual no editor (timeout 10s,
// sem audit, sem métricas).
export interface GenericToolTestResult {
  success: boolean
  statusCode: number | null
  durationMs: number
  url: string
  method: string
  requestBody: string | null
  requestHeaders: Record<string, string>
  responseBody: string | null
  responseTruncated: boolean
  responseHeaders: Record<string, string>
  parsedData: unknown
  /** Response após drop-extras + validação contra outputSchema. Null quando há schemaErrors. */
  projectedData: unknown
  /** Violations detectadas (vazio quando passa ou modo Off). */
  schemaErrors: string[]
  /** True quando o projector NÃO aplicou validação (modo Off, schema ausente, parse falhou). */
  projectionBypassed: boolean
  error: string | null
}

export const executeGenericTool = (id: string, args: Record<string, unknown>) =>
  post<GenericToolTestResult>(`/generic-tools/${id}/execute`, { args })

/**
 * Testa uma configuração de tool ANTES dela ser persistida. UI usa pra
 * gatear a habilitação do botão "Criar/Salvar" — PM tem que ver 2xx no
 * endpoint real antes de salvar.
 */
export const testDraftGenericTool = (
  tool: CreateGenericToolBody,
  args: Record<string, unknown>,
) => post<GenericToolTestResult>('/generic-tools/test-draft', { tool, args })

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
