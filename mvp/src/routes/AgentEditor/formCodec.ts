import type { AgentDraft, AgentDraftPayload, AgentToolDefinition, AgentType } from '../../api/agentDrafts'
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

// Chave em payload.metadata que carrega o domínio do Worker. Mesmo valor
// usado pelo backend (AgentDefinition.WorkerScopeMetadataKey).
const WORKER_SCOPE_METADATA_KEY = 'x-worker-scope'

// Chave em payload.metadata que declara HITL pro Tool Runner. Mesmo valor
// usado pelo backend (AgentDefinition.ToolRunnerHitlRequiredMetadataKey).
const TOOL_RUNNER_HITL_METADATA_KEY = 'x-tool-runner-hitl-required'

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
    type: 'Custom',
    routerIntentIds: [],
    workerScope: '',
    toolRunnerHitlRequired: false,
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
    currentStep: 'type',
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

  // Backend default = Custom quando ausente. Aceita Custom|Router|Worker|
  // ToolRunner no UI; tipos não-implementados (futuro Conversational) caem
  // pra Custom — evita travar o wizard com value inválido.
  const rawType = payload.type
  const type: AgentType =
    rawType === 'Router'
      ? 'Router'
      : rawType === 'Worker'
        ? 'Worker'
        : rawType === 'ToolRunner'
          ? 'ToolRunner'
          : 'Custom'

  // IDs das intents que este Router atende. Source of truth = backend
  // (junction aihub.agent_router_intents); response do agent já traz no
  // campo routerIntentIds. Pra drafts (que não tocam no agent_definitions
  // ainda), o campo viaja no payload.routerIntentIds.
  const rawIntentIds = (payload as { routerIntentIds?: unknown }).routerIntentIds
  const routerIntentIds = Array.isArray(rawIntentIds)
    ? (rawIntentIds.filter((v) => typeof v === 'string') as string[])
    : []

  // Scope do Worker vive em metadata['x-worker-scope']. Lê do payload e
  // hidrata no form. Outros tipos não usam essa chave — fica string vazia.
  const rawMetadataScope = payload.metadata?.[WORKER_SCOPE_METADATA_KEY]
  const workerScope = typeof rawMetadataScope === 'string' ? rawMetadataScope : ''

  // Flag HITL do Tool Runner vive em metadata['x-tool-runner-hitl-required'].
  // Backend grava string "true"/ausente; UI mantém como boolean no FormState.
  const rawHitl = payload.metadata?.[TOOL_RUNNER_HITL_METADATA_KEY]
  const toolRunnerHitlRequired =
    typeof rawHitl === 'string' && rawHitl.toLowerCase() === 'true'

  // Worker e Tool Runner gravam o schema em payload.structuredOutput (não
  // no instructions como Custom-advanced). Hidrata o FormState a partir
  // dele pra que o OutputStep mostre o schema editável ao reabrir.
  let workerOutput: StructuredSection | null = null
  if (type === 'Worker' || type === 'ToolRunner') {
    const so = payload.structuredOutput
    if (so && so.schema) {
      try {
        const schemaText = JSON.stringify(so.schema, null, 2)
        workerOutput = {
          mode: 'structured',
          description: so.schemaDescription ?? '',
          schema: schemaText,
        }
      } catch {
        workerOutput = null
      }
    }
  }

  return {
    name: payload.name ?? draft.name ?? '',
    type,
    routerIntentIds,
    workerScope,
    toolRunnerHitlRequired,
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
    output: workerOutput ?? {
      mode: outputHasContent ? 'structured' : 'text',
      description: decoded.output.description,
      schema: decoded.output.schema,
    },
    // Worker e Tool Runner são sempre tratados como "advanced" (steps fixos
    // pelo tipo): força o modo independente do conteúdo do instructions,
    // pra que toggles e hidratação de visited steps fiquem consistentes ao
    // reabrir o draft.
    agentMode:
      type === 'Worker' || type === 'ToolRunner'
        ? 'advanced'
        : decoded.hasStructured || memEnabled
          ? 'advanced'
          : 'basic',
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

  // Router usa placeholder deterministico — runtime (ChatOptionsBuilder)
  // resolve o conteúdo real ao montar o prompt baseado no set vivo do join
  // agent_router_intents. Worker usa skeleton mínimo — runtime injeta o
  // bloco "# Domínio de análise" lendo metadata['x-worker-scope']. Tool
  // Runner usa skeleton mínimo — tools já carregam Description/WhenToUse
  // pro LLM via FunctionTool factory, sem necessidade de bloco anchor
  // adicional. Custom segue o encoder genérico do ProfileStep.
  const instructions =
    form.type === 'Router'
      ? encodeRouterInstructions(form.name)
      : form.type === 'Worker'
        ? encodeWorkerInstructions(form.name)
        : form.type === 'ToolRunner'
          ? encodeToolRunnerInstructions(form.name)
          : encodeInstructions(
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
  const structuredOutput = encodeStructuredOutput(prev, form)

  // Drafts novos não persistem mais 'x-router-intents' no metadata.
  // Refs do agent → intents do pool vivem em aihub.agent_router_intents
  // (junction). Removemos a chave legacy se vier do prev pra higienizar.
  // Worker grava 'x-worker-scope' aqui (texto livre lido em runtime); pra
  // outros tipos a chave é removida pra evitar lixo cross-tipo. Tool
  // Runner declara HITL em 'x-tool-runner-hitl-required' (mesma higiene).
  const metadata = encodeToolRunnerHitlMetadata(
    encodeWorkerScopeMetadata(
      stripLegacyRouterIntentsMetadata(prev?.metadata),
      form,
    ),
    form,
  )

  // Pra Router, viaja routerIntentIds no payload. Esse campo é consumido
  // pelo controller (POST/PUT /agents) que reconcilia a junction após o
  // upsert. Pra Custom, omite (controller ignora).
  const payload: AgentDraftPayload = {
    ...(prev ?? {}),
    name: form.name.trim(),
    description: prev?.description ?? null,
    type: form.type,
    instructions,
    model: nextModel,
    tools: mergedTools,
    structuredOutput,
    operationalMemory,
    middlewares: mergedMiddlewares,
    metadata,
  }
  if (form.type === 'Router') {
    ;(payload as Record<string, unknown>).routerIntentIds = [...form.routerIntentIds]
  } else {
    delete (payload as Record<string, unknown>).routerIntentIds
  }
  return payload
}

/**
 * Schema de output do Router. Persiste com <c>intent</c> sem enum — runtime
 * (ChatOptionsBuilder) injeta o enum dinâmico baseado nas intents resolvidas
 * pelo lookup do join. Confidence/rationale ficam fixos pra dar pro caller
 * dos workflows um shape estável (predicates leem <c>$.intent</c>).
 *
 * Worker serializa o schema editado em <c>form.output</c> pro
 * <c>payload.structuredOutput</c> (envia json_schema real pro LLM). Schema
 * inválido cai no <c>prev</c> pra preservar último estado válido. Custom
 * NÃO usa <c>payload.structuredOutput</c> — output rico pra Custom é
 * embutido como texto no <c>instructions</c> via <c>encodeInstructions</c>.
 */
function encodeStructuredOutput(
  prev: AgentDraftPayload | undefined,
  form: FormState,
): AgentDraftPayload['structuredOutput'] {
  if (form.type === 'Worker' || form.type === 'ToolRunner') {
    if (form.output.mode !== 'structured') {
      return null
    }
    const trimmedSchema = form.output.schema.trim()
    if (!trimmedSchema) return prev?.structuredOutput ?? null
    let parsed: unknown
    try {
      parsed = JSON.parse(trimmedSchema)
    } catch {
      return prev?.structuredOutput ?? null
    }
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
      return prev?.structuredOutput ?? null
    }
    const trimmedDesc = form.output.description.trim()
    const prevSchemaName =
      typeof prev?.structuredOutput?.schemaName === 'string'
        ? prev.structuredOutput.schemaName
        : null
    const fallbackSchemaName = form.type === 'Worker' ? 'WorkerOutput' : 'ToolRunnerOutput'
    return {
      responseFormat: 'json_schema',
      schemaName: prevSchemaName || fallbackSchemaName,
      schemaDescription: trimmedDesc.length > 0 ? trimmedDesc : null,
      schema: parsed,
    }
  }

  if (form.type !== 'Router') {
    const prevSO = prev?.structuredOutput
    return prevSO === undefined ? null : prevSO
  }

  return {
    responseFormat: 'json_schema',
    schemaName: 'router_intent',
    schema: {
      type: 'object',
      properties: {
        intent: {
          type: 'string',
          description:
            "Identificador da categoria escolhida — ver bloco '# Intenções disponíveis' no system prompt para definição de cada uma. Runtime injeta o enum dinâmico.",
        },
        confidence: {
          type: 'number',
          description:
            'Confiança do modelo na classificação, entre 0 e 1. Valores < 0.5 sinalizam ambiguidade — workflow caller deve rotear pra fluxo de fallback ou humano.',
          minimum: 0,
          maximum: 1,
        },
        reason: {
          type: 'string',
          description:
            'Pensamento que levou à escolha: qual sinal do input apontou pra essa categoria e por que descartou as outras. 1-3 frases concisas, sem repetir o input.',
        },
      },
      required: ['intent', 'confidence', 'reason'],
      additionalProperties: false,
    },
  }
}

/**
 * Higieniza a chave 'x-router-intents' do metadata herdado do prev. Drafts
 * antigos podem ter o campo gravado pelo wizard anterior; removê-lo no save
 * evita confusão (a fonte da verdade agora é a junction).
 */
function stripLegacyRouterIntentsMetadata(
  prev: Record<string, string> | null | undefined,
): Record<string, string> {
  const base: Record<string, string> = { ...(prev ?? {}) }
  delete base['x-router-intents']
  return base
}

/**
 * Gera instructions skeleton pro Router. O bloco de intenções (com
 * descrições/exemplos) é resolvido em runtime pelo <c>ChatOptionsBuilder</c>
 * e anexado ao final deste skeleton — o user pode editar livremente role/goal/
 * output sem quebrar a injeção (não há marker frágil; runtime apenas
 * concatena com separador).
 */
/**
 * Skeleton determinístico mínimo do Tool Runner. Tool Runner é executor —
 * tools selecionadas já viajam com Description/WhenToUse via
 * <c>FunctionTool</c> factory; o LLM lê os docs delas direto. Manter o
 * skeleton enxuto evita duplicar instruções de uso de tool no system
 * prompt e dá determinismo ao prompt persistido.
 */
export function encodeToolRunnerInstructions(name: string): string {
  const cleanName = (name || '').trim()
  const role = cleanName
    ? `Você é ${cleanName}.`
    : 'Você é um Tool Runner (executor de tarefas via tools).'
  return [
    role,
    'Use as ferramentas disponíveis pra executar a tarefa solicitada.',
    'Escolha tools com base na descrição de cada uma; valide argumentos antes de chamar.',
    'Quando a tarefa estiver completa, devolva um resumo curto da execução.',
  ].join(' ')
}

/**
 * Skeleton determinístico mínimo do Worker. O domínio (scope) NÃO entra
 * aqui — vive em <c>metadata['x-worker-scope']</c> e é injetado em runtime
 * pelo <c>ChatOptionsBuilder</c> (bloco "# Domínio de análise" anexado ao
 * final). Manter o skeleton enxuto evita duplicação quando o user edita
 * scope sem perceber que também tem texto análogo no instructions.
 */
export function encodeWorkerInstructions(name: string): string {
  const cleanName = (name || '').trim()
  const role = cleanName
    ? `Você é ${cleanName}.`
    : 'Você é um Worker (specialist de domínio).'
  return [
    role,
    'Produza análise estruturada conforme o schema fornecido.',
    'Mantenha-se dentro do domínio declarado.',
  ].join(' ')
}

/**
 * Mantém <c>metadata['x-worker-scope']</c> sincronizado com o form. Pra
 * Worker grava o scope (ou remove a chave quando vazio); pra outros tipos
 * remove a chave pra evitar lixo cross-tipo (Custom/Router herdando scope
 * de uma transição anterior).
 */
function encodeWorkerScopeMetadata(
  prev: Record<string, string>,
  form: FormState,
): Record<string, string> {
  const next: Record<string, string> = { ...prev }
  if (form.type === 'Worker') {
    const trimmed = form.workerScope.trim()
    if (trimmed.length > 0) next[WORKER_SCOPE_METADATA_KEY] = trimmed
    else delete next[WORKER_SCOPE_METADATA_KEY]
  } else {
    delete next[WORKER_SCOPE_METADATA_KEY]
  }
  return next
}

/**
 * Mantém <c>metadata['x-tool-runner-hitl-required']</c> sincronizado com o
 * form. Pra Tool Runner grava <c>"true"</c> quando o flag está on (ou
 * remove a chave quando off); pra outros tipos remove a chave pra evitar
 * lixo cross-tipo (Custom herdando flag de uma transição anterior).
 */
function encodeToolRunnerHitlMetadata(
  prev: Record<string, string>,
  form: FormState,
): Record<string, string> {
  const next: Record<string, string> = { ...prev }
  if (form.type === 'ToolRunner') {
    if (form.toolRunnerHitlRequired) next[TOOL_RUNNER_HITL_METADATA_KEY] = 'true'
    else delete next[TOOL_RUNNER_HITL_METADATA_KEY]
  } else {
    delete next[TOOL_RUNNER_HITL_METADATA_KEY]
  }
  return next
}

export function encodeRouterInstructions(name: string): string {
  const cleanName = (name || '').trim()
  const role = cleanName
    ? `Classificador de intenções (${cleanName}). Recebe a mensagem do usuário e devolve a categoria que melhor descreve.`
    : 'Classificador de intenções. Recebe a mensagem do usuário e devolve a categoria que melhor descreve.'

  return [
    '# Role',
    role,
    '',
    '# Goal',
    'Para cada input, escolher exatamente uma categoria do enum `intent` que melhor descreva o conteúdo. ' +
      'Quando nenhuma categoria encaixar bem, escolher a mais próxima e devolver `confidence < 0.5` ' +
      'para sinalizar ambiguidade ao workflow caller.',
    '',
    '# Output',
    'Retorne sempre o objeto estruturado definido no schema:',
    '- `intent`: exatamente um valor do enum (não invente, não combine).',
    '- `confidence`: número entre 0 e 1; <0.5 quando ambíguo.',
    '- `reason`: 1-3 frases curtas explicando qual sinal do input levou a essa categoria e por que descartou as outras.',
    'Não escreva texto fora do JSON.',
  ].join('\n')
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
