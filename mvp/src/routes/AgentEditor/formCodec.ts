import type { AgentDraft, AgentDraftPayload, AgentToolDefinition } from '../../api/agentDrafts'
import type { GenericTool, ParamDefinition } from '../../api/genericTools'
import type { McpServer } from '../../api/mcpServers'
import type { KvRow } from '../../components/PostmanEditor/KvTable'
import { decodeInstructions, encodeInstructions, type ToolDescriptor } from './instructionsCodec'
import type { FormState, StructuredSection } from './types'

// Type discriminante de uma entry de payload.middlewares[]. Espelha
// EfsAiHub.Core.Agents.AgentMiddlewareConfig — só os campos que mexemos. Demais
// chaves viajam intactas via spread quando preservamos entries de outros tipos.
interface MiddlewareConfigEntry {
  type?: string
  enabled?: boolean
  settings?: Record<string, string>
  [key: string]: unknown
}

const SECURITY_MIDDLEWARE_TYPE = 'SecurityGuardrails'

export function shortId(): string {
  return Math.random().toString(36).slice(2, 10)
}

export function toggleId(list: string[], id: string): string[] {
  return list.includes(id) ? list.filter((x) => x !== id) : [...list, id]
}

// Mantém entries não-managed do `tools` original e substitui as entries
// type=generic_http/type=mcp pela seleção corrente da tela. Reusa entries
// existentes (preserva campos como requiresApproval) pra evitar churn no
// canonical hash do AgentVersion no publish.
export function mergeTools(
  prev: AgentToolDefinition[] | null,
  toolIds: string[],
  mcpIds: string[],
): AgentToolDefinition[] {
  const kept: AgentToolDefinition[] = []
  const prevById = new Map<string, AgentToolDefinition>()
  if (prev) {
    for (const entry of prev) {
      if (entry.type === 'generic_http' && typeof entry.genericToolId === 'string') {
        prevById.set(`tool:${entry.genericToolId}`, entry)
        continue
      }
      if (entry.type === 'mcp' && typeof entry.mcpServerId === 'string') {
        prevById.set(`mcp:${entry.mcpServerId}`, entry)
        continue
      }
      kept.push(entry)
    }
  }

  const next: AgentToolDefinition[] = [...kept]
  for (const id of toolIds) {
    const existing = prevById.get(`tool:${id}`)
    next.push(existing ?? { type: 'generic_http', genericToolId: id, requiresApproval: false })
  }
  for (const id of mcpIds) {
    const existing = prevById.get(`mcp:${id}`)
    next.push(existing ?? { type: 'mcp', mcpServerId: id, requiresApproval: false })
  }
  return next
}

function describeParam(name: string, def: ParamDefinition, location: 'path' | 'query'): string {
  const required = def.required ? 'obrigatório' : 'opcional'
  const type = def.type || 'string'
  const desc = def.description ? ` — ${def.description}` : ''
  return `- \`${name}\` (${location}, ${type}, ${required})${desc}`
}

// Constrói o bloco completo de doc da tool HTTP: endpoint + parâmetros (path/
// query) + body (quando aplicável) + resposta. Self-contained com seus próprios
// labels — o encoder não envolve em "**Input esperado:**" pra evitar bold-em-
// bold no markdown final.
function formatGenericToolDetails(tool: GenericTool): string | undefined {
  const parts: string[] = []
  parts.push(`**Endpoint:** \`${tool.httpMethod} ${tool.urlTemplate}\``)

  const pathEntries = Object.entries(tool.pathParams)
  const queryEntries = Object.entries(tool.queryParams)
  if (pathEntries.length > 0 || queryEntries.length > 0) {
    parts.push('')
    parts.push('**Parâmetros:**')
    for (const [name, def] of pathEntries) {
      parts.push(describeParam(name, def, 'path'))
    }
    for (const [name, def] of queryEntries) {
      parts.push(describeParam(name, def, 'query'))
    }
  }

  if (tool.inputContentType === 'Json' && tool.inputSchema) {
    parts.push('')
    parts.push('**Body (JSON):**')
    parts.push('```json')
    parts.push(tool.inputSchema)
    parts.push('```')
  } else if (tool.inputContentType === 'FormUrlEncoded' && tool.inputSchema) {
    parts.push('')
    parts.push('**Body (form-urlencoded):**')
    parts.push('```json')
    parts.push(tool.inputSchema)
    parts.push('```')
  } else if (tool.inputContentType === 'Text' && tool.inputSchema) {
    parts.push('')
    parts.push(`**Body:** texto puro (campo \`${tool.inputSchema}\`)`)
  }

  if (tool.outputContentType === 'Json' && tool.outputSchema) {
    parts.push('')
    parts.push('**Resposta (JSON):**')
    parts.push('```json')
    parts.push(tool.outputSchema)
    parts.push('```')
  } else if (tool.outputContentType === 'Csv' && tool.outputSchema) {
    parts.push('')
    parts.push('**Resposta (CSV):**')
    parts.push('```json')
    parts.push(tool.outputSchema)
    parts.push('```')
  } else if (tool.outputContentType === 'Text' && tool.outputSchema) {
    parts.push('')
    parts.push(`**Resposta:** ${tool.outputSchema}`)
  }

  return parts.join('\n')
}

function genericToolToDescriptor(tool: GenericTool): ToolDescriptor {
  return {
    name: tool.name,
    description: tool.description,
    whenToUse: tool.whenToUse ?? undefined,
    details: formatGenericToolDetails(tool),
  }
}

function mcpToDescriptor(mcp: McpServer): ToolDescriptor {
  const parts: string[] = []
  if (mcp.allowedTools.length > 0) {
    parts.push(
      `**Tools disponíveis:** ${mcp.allowedTools.map((t) => `\`${t}\``).join(', ')}`,
    )
  } else {
    parts.push('**Tools disponíveis:** descobertas dinamicamente pelo servidor.')
  }
  if (mcp.requireApproval === 'always') {
    parts.push('')
    parts.push('⚠️ **Requer aprovação humana** antes de cada execução.')
  }
  return {
    name: mcp.name || mcp.serverLabel,
    description: mcp.description ?? '',
    details: parts.length > 0 ? parts.join('\n') : undefined,
  }
}

// Constrói descritores na ordem (tools genéricas primeiro, depois MCPs) com
// base na seleção atual e no catálogo carregado. Ids que não baterem com o
// catálogo (tool deletada externamente, etc.) são silenciosamente ignorados.
export function buildToolDescriptors(
  toolIds: string[],
  mcpIds: string[],
  toolsCatalog: GenericTool[],
  mcpsCatalog: McpServer[],
): ToolDescriptor[] {
  const result: ToolDescriptor[] = []
  for (const id of toolIds) {
    const t = toolsCatalog.find((x) => x.id === id)
    if (t) result.push(genericToolToDescriptor(t))
  }
  for (const id of mcpIds) {
    const m = mcpsCatalog.find((x) => x.id === id)
    if (m) result.push(mcpToDescriptor(m))
  }
  return result
}

function emptySection(): StructuredSection {
  return { mode: 'text', description: '', schema: '' }
}

function emptyMemorySection() {
  return { enabled: false, schema: '' }
}

export function emptyFormState(): FormState {
  return {
    name: '',
    predefinedModelId: '',
    profile: {
      role: '',
      goal: '',
      backstory: '',
      rules: [],
      constraints: [],
    },
    toolIds: [],
    mcpIds: [],
    security: { enabled: false },
    memory: emptyMemorySection(),
    input: emptySection(),
    output: emptySection(),
    agentMode: 'basic',
    currentStep: 'profile',
    metadataRows: [],
  }
}

function metadataRowsFromPayload(payload: AgentDraftPayload | undefined): KvRow<string>[] {
  const metadata = payload?.metadata ?? null
  if (!metadata) return []
  return Object.entries(metadata).map(([key, val]) => ({
    id: shortId(),
    key,
    val: typeof val === 'string' ? val : String(val ?? ''),
  }))
}

export function fromDraft(draft: AgentDraft): FormState {
  const payload = draft.payload ?? {}
  const decoded = decodeInstructions(payload.instructions ?? null)

  const tools = payload.tools ?? []
  const toolIds: string[] = []
  const mcpIds: string[] = []
  for (const t of tools) {
    if (t.type === 'generic_http' && typeof t.genericToolId === 'string' && t.genericToolId) {
      toolIds.push(t.genericToolId)
    } else if (t.type === 'mcp' && typeof t.mcpServerId === 'string' && t.mcpServerId) {
      mcpIds.push(t.mcpServerId)
    }
  }

  const inputHasContent = decoded.input.description.length > 0 || decoded.input.schema.length > 0
  const outputHasContent = decoded.output.description.length > 0 || decoded.output.schema.length > 0

  const mem = payload.operationalMemory ?? null
  const memEnabled = mem !== null && mem.schema !== null && mem.schema !== undefined
  const memSchemaText = memEnabled
    ? JSON.stringify(mem!.schema, null, 2)
    : ''

  // Round-trip via payload.middlewares[]. Outras entries (AccountGuard,
  // StructuredOutputState) são opaque pra esta camada — preservadas via spread
  // no buildPayload sem necessidade de leitura aqui.
  const rawMiddlewares = payload.middlewares
  const middlewareList: MiddlewareConfigEntry[] = Array.isArray(rawMiddlewares)
    ? (rawMiddlewares as MiddlewareConfigEntry[])
    : []
  const securityEntry = middlewareList.find((m) => m?.type === SECURITY_MIDDLEWARE_TYPE)
  const securityEnabled = securityEntry?.enabled === true

  return {
    name: payload.name ?? draft.name ?? '',
    predefinedModelId: payload.model?.predefinedModelId ?? '',
    profile: decoded.profile,
    toolIds,
    mcpIds,
    security: { enabled: securityEnabled },
    memory: {
      enabled: memEnabled,
      schema: memSchemaText,
    },
    input: {
      mode: inputHasContent ? 'structured' : 'text',
      description: decoded.input.description,
      schema: decoded.input.schema,
    },
    output: {
      mode: outputHasContent ? 'structured' : 'text',
      description: decoded.output.description,
      schema: decoded.output.schema,
    },
    agentMode: decoded.hasStructured || memEnabled ? 'advanced' : 'basic',
    currentStep: 'profile',
    metadataRows: metadataRowsFromPayload(payload),
  }
}

// Constrói o payload do PUT preservando todos os campos do draft existente
// (middlewares/skillRefs/structuredOutput/etc.). A tela edita instructions/
// tools (generic_http/mcp)/model.predefinedModelId/name. Demais campos viajam
// intactos via spread. O catálogo de tools/mcps é usado APENAS pra encodar a
// seção "Ferramentas disponíveis" no instructions (documentação pro LLM); a
// referência por id em payload.tools[] é gerida por mergeTools.
export function buildPayload(
  prev: AgentDraftPayload | undefined,
  form: FormState,
  toolsCatalog: GenericTool[],
  mcpsCatalog: McpServer[],
): AgentDraftPayload {
  const includeStructured = form.agentMode === 'advanced'
  const inputForCodec = form.input.mode === 'structured' ? form.input : { description: '', schema: '' }
  const outputForCodec = form.output.mode === 'structured' ? form.output : { description: '', schema: '' }
  const toolDocs = buildToolDescriptors(form.toolIds, form.mcpIds, toolsCatalog, mcpsCatalog)
  const instructions = encodeInstructions(
    form.profile,
    inputForCodec,
    outputForCodec,
    toolDocs,
    includeStructured,
  )

  // AgentModelConfig.DeploymentName é `required` no backend (System.Text.Json
  // valida no bind). Cliente envia '' quando só usa preset — o
  // PredefinedModelBinder resolve em runtime.
  const prevModel = prev?.model ?? null
  const prevDeploymentName =
    typeof prevModel?.deploymentName === 'string' ? (prevModel.deploymentName as string) : ''
  const trimmedModelId = form.predefinedModelId.trim()
  const nextModel: AgentDraftPayload['model'] = {
    ...(prevModel ?? {}),
    deploymentName: prevDeploymentName,
    predefinedModelId: trimmedModelId || null,
  }

  const mergedTools = mergeTools(prev?.tools ?? null, form.toolIds, form.mcpIds)
  const operationalMemory = encodeOperationalMemory(form.memory)
  const mergedMiddlewares = mergeMiddlewares(prev?.middlewares, form.security)

  return {
    ...(prev ?? {}),
    name: form.name.trim(),
    description: prev?.description ?? null,
    instructions,
    model: nextModel,
    tools: mergedTools,
    operationalMemory,
    middlewares: mergedMiddlewares,
  }
}

/**
 * Garante que payload.middlewares[] reflita o toggle de Segurança preservando
 * todas as outras entries (AccountGuard etc.). Toggle off = entry removida do
 * array, sem ficar zumbi com enabled:false.
 */
function mergeMiddlewares(
  prevMiddlewares: unknown,
  security: FormState['security'],
): MiddlewareConfigEntry[] {
  const list: MiddlewareConfigEntry[] = Array.isArray(prevMiddlewares)
    ? (prevMiddlewares as MiddlewareConfigEntry[])
    : []
  const others = list.filter((m) => m?.type !== SECURITY_MIDDLEWARE_TYPE)
  if (!security.enabled) return others
  return [...others, { type: SECURITY_MIDDLEWARE_TYPE, enabled: true, settings: {} }]
}

/**
 * Serializa o estado do step "Memória" pro payload do draft. Schema string
 * vira objeto JSON real (backend espera JsonDocument). Inválido OU desligado
 * vira `null` — limpa qualquer config anterior preservada via spread.
 */
function encodeOperationalMemory(memory: FormState['memory']): AgentDraftPayload['operationalMemory'] {
  if (!memory.enabled) return null
  const trimmed = memory.schema.trim()
  if (!trimmed) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(trimmed)
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object') return null
  return { schema: parsed }
}
