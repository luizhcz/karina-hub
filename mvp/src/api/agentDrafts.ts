import { get, post, put } from './client'

// Tipos espelham EfsAiHub.Core.Agents.AgentDraftPayload e o response do
// AgentDraftsController. Payload é deliberadamente "open" ([key: string]:
// unknown): a tela só edita name/description/instructions/model.predefinedModelId/
// metadata, mas drafts forkados de agente existente trazem tools/middlewares/
// skills/etc. que precisam viajar intactos nos PUTs (deep-spread defensivo no
// editor).

export type AgentDraftStatus = 'Draft' | 'PendingApproval' | 'Rejected'

export interface AgentDraftModelConfig {
  // `deploymentName` é `required` no backend (System.Text.Json valida no
  // bind). Cliente envia '' quando só usa preset — o PredefinedModelBinder
  // resolve em runtime. Mantemos a coluna preenchida (mesmo que vazia) pra
  // satisfazer a deserialização.
  deploymentName?: string
  predefinedModelId?: string | null
  // Demais campos do AgentModelConfig (temperature, maxTokens) chegam aqui
  // quando o draft veio de um agent já publicado — preservados via spread no
  // update, sem render na tela.
  [key: string]: unknown
}

// Espelho de EfsAiHub.Core.Agents.AgentToolDefinition (camelCase via API). Cada
// entrada do array `tools` tem um `type` discriminante: a tela manipula apenas
// "generic_http" (referencia GenericTool por id) e "mcp" (referencia McpServer
// por id). Outros tipos legados (function/file_search/web_search/etc.) viajam
// intactos pelo spread defensivo no update.
export interface AgentToolDefinition {
  type: string
  name?: string | null
  requiresApproval?: boolean
  genericToolId?: string | null
  mcpServerId?: string | null
  // Campos legacy/fallback usados quando o draft não usa referência por id —
  // preservados sem manipulação pela tela.
  [key: string]: unknown
}

export interface AgentDraftStructuredOutput {
  /** "text" | "json" | "json_schema" */
  responseFormat?: string
  schemaName?: string | null
  schemaDescription?: string | null
  /** JSON Schema (objeto serializado já parseado em JS). */
  schema?: unknown | null
}

export interface AgentDraftOperationalMemory {
  /**
   * JSON Schema do payload da memória. Quando presente, ativa o middleware
   * server-side. `null` ou ausente = memória desligada.
   */
  schema?: unknown | null
  maxBytes?: number | null
}

// Espelho de EfsAiHub.Core.Agents.AgentType. Custom é o default quando ausente
// no payload (back-end aplica o default na desserialização).
export type AgentType = 'Custom' | 'Router'

export interface AgentDraftPayload {
  name?: string | null
  description?: string | null
  type?: AgentType | null
  instructions?: string | null
  model?: AgentDraftModelConfig | null
  tools?: AgentToolDefinition[] | null
  structuredOutput?: AgentDraftStructuredOutput | null
  operationalMemory?: AgentDraftOperationalMemory | null
  metadata?: Record<string, string> | null
  /**
   * Set de IDs de intents do pool global que este Router atende. Aplicável
   * apenas quando type='Router'; ignorado pra Custom. Persistido no backend
   * via junction aihub.agent_router_intents — controller reconcilia após o
   * upsert do agent.
   */
  routerIntentIds?: string[] | null
  // Demais campos (provider, middlewares, skillRefs, etc.) são opaque pra esta
  // camada — preservados intactos no update.
  [key: string]: unknown
}

export interface AgentDraft {
  id: string
  name: string
  payload: AgentDraftPayload
  projectId: string
  tenantId: string
  status: AgentDraftStatus
  rejectionFeedback?: string | null
  submittedAt?: string | null
  createdAt: string
  updatedAt: string
  createdBy?: string | null
  isEditDraft: boolean
  baseAgentId?: string | null
  baseRevision?: number | null
}

export interface CreateAgentDraftBody {
  id?: string
  payload: AgentDraftPayload
}

export interface UpdateAgentDraftBody {
  payload: AgentDraftPayload
  expectedUpdatedAt: string
}

const BASE = '/agent-drafts'

export const listAgentDrafts = () => get<AgentDraft[]>(BASE)
export const getAgentDraft = (id: string) => get<AgentDraft>(`${BASE}/${id}`)
export const createAgentDraft = (body: CreateAgentDraftBody) =>
  post<AgentDraft>(BASE, body)
export const updateAgentDraft = (id: string, body: UpdateAgentDraftBody) =>
  put<AgentDraft>(`${BASE}/${id}`, body)
export interface SubmitAgentDraftResult {
  draft: AgentDraft
  autoApproved: boolean
  tier?: 'Cosmetic' | 'Behavioral' | null
}

export const submitAgentDraft = (id: string) =>
  post<SubmitAgentDraftResult>(`${BASE}/${id}/submit`, {})

// Gera GUID pra novo rascunho. Mesma estratégia do ToolEditor — backend aceita
// qualquer string como Id (slug user-friendly também funciona), mas a tela
// não expõe edição de Id pra quem cria o agente.
export function generateDraftId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  const segment = () => Math.random().toString(36).slice(2, 10)
  return `${segment()}-${segment()}-${segment()}-${segment()}`
}
