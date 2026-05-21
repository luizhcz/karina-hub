import { get, patch, post, put } from './client'
import type { AgentDraft, AgentToolDefinition } from './agentDrafts'

// Tipos espelham AgentResponse do backend (camelCase via API). Campos com '?'
// refletem propriedades opcionais ou que podem vir null/ausentes em agentes
// minimalistas. Não exponho todos os campos do payload — só os que a UI usa.

export type AgentVisibility = 'project' | 'global'

// Espelha EfsAiHub.Core.Agents.AgentType. Custom é o default no backend
// (agentes legacy sem campo type no jsonb hidratam como Custom).
export type AgentType = 'Custom' | 'Router' | 'Worker' | 'ToolRunner' | 'Conversational'

export interface AgentModel {
  predefinedModelId?: string | null
  deploymentName?: string
  temperature?: number | null
  maxTokens?: number | null
  [key: string]: unknown
}

export interface AgentProvider {
  type?: string
  clientType?: string
  endpoint?: string | null
  [key: string]: unknown
}

// Entry de definition.middlewares[]. Espelha EfsAiHub.Core.Agents.AgentMiddlewareConfig.
// `type` ∈ ValidMiddlewareTypes do backend ("AccountGuard" | "StructuredOutputState" |
// "SecurityGuardrails"). Settings é dict key→value string (defaults vêm do catálogo
// em GET /functions middlewareTypes[].settings[].defaultValue).
export interface AgentMiddleware {
  type: string
  enabled: boolean
  settings: Record<string, string>
}

export interface AgentOperationalMemory {
  /**
   * JSON Schema do payload da memória. Quando presente, ativa o middleware
   * server-side. `null` ou ausente = memória desligada.
   */
  schema?: unknown | null
  maxBytes?: number | null
}

export interface Agent {
  id: string
  name: string
  description?: string | null
  type?: AgentType
  model?: AgentModel | null
  provider?: AgentProvider | null
  instructions?: string | null
  tools?: AgentToolDefinition[] | null
  operationalMemory?: AgentOperationalMemory | null
  middlewares?: AgentMiddleware[] | null
  visibility: AgentVisibility
  enabled: boolean
  originProjectId?: string | null
  originTenantId?: string | null
  allowedProjectIds?: string[] | null
  metadata?: Record<string, string> | null
  /**
   * Soft warnings da última validação. Presente apenas no response do
   * Create/Update/Validate — listagens não retornam (cálculo on-demand).
   */
  warnings?: string[] | null
  /**
   * IDs das intents do pool global (aihub.router_intents) que este Router
   * atende. Populado apenas quando type='Router'. Resolvido on-demand a
   * partir da junction aihub.agent_router_intents.
   */
  routerIntentIds?: string[] | null
  /**
   * Gate "validated for chat" — populado quando um admin valida o agente em
   * Chat Sandbox. Zerado automaticamente pelo backend quando nova AgentVersion
   * é publicada. Frontend só lê e renderiza badge/warning.
   */
  lastChatSandboxValidatedAt?: string | null
  lastChatSandboxValidatedByUserId?: string | null
  lastChatSandboxValidatedAgentVersionId?: string | null
  createdAt: string
  updatedAt: string
}

/**
 * @param scope `'project'` retorna apenas agentes do ProjectId atual. Sem o param,
 *  inclui também `Visibility=global` de outros projetos do mesmo tenant — usado em
 *  runtime/consumo cross-project.
 */
export const listAgents = (scope?: 'project') => {
  const qs = scope ? `?scope=${scope}` : ''
  return get<Agent[]>(`/agents${qs}`)
}
export const getAgent = (id: string) => get<Agent>(`/agents/${id}`)

// Cria um draft de edição a partir do agent publicado. Backend retorna o
// AgentDraft já persistido (com isEditDraft=true, baseAgentId e baseRevision
// apontando pro snapshot atual). Caller normalmente navega pra
// /agentes/{returnedDraft.id} pra editar no wizard.
export const createEditDraft = (id: string) =>
  post<AgentDraft>(`/agents/${id}/edit-draft`, {})

// Trilha unificada de governança do agent — drafts (Submitted/Approved/
// Rejected/AutoApproved) + AdminOverride aplicado via PUT direto. Ordem por
// OccurredAt asc.
export type ApprovalAction =
  | 'Submitted'
  | 'Resubmitted'
  | 'Approved'
  | 'Rejected'
  | 'AutoApproved'
  | 'AdminOverride'

export type ChangeTier = 'Cosmetic' | 'Behavioral'

export interface ApprovalHistoryEntry {
  id: string
  draftId: string
  agentDefinitionId?: string | null
  action: ApprovalAction
  actorUserId: string
  feedback?: string | null
  tier?: ChangeTier | null
  occurredAt: string
}

export const getApprovalHistory = (id: string) =>
  get<ApprovalHistoryEntry[]>(`/agents/${id}/approval-history`)

// Liga/desliga o agent publicado. Não passa pelo fluxo de aprovação por design
// (kill switch). Backend audita before/after + reason. Ownership é garantida
// pelo HasQueryFilter — PM só consegue mexer em agentes do próprio projeto.
export interface UpdateAgentEnabledBody {
  enabled: boolean
  reason?: string | null
}

export const updateAgentEnabled = (id: string, body: UpdateAgentEnabledBody) =>
  patch<Agent>(`/agents/${id}/enabled`, body)

// PUT admin direto — atualiza agente publicado bypassando draft+approval.
// Backend (AgentsController.Update) registra Action='AdminOverride' em
// agent_approval_history + AdminAuditLog. 403 pra non-admin.
//
// Shape espelha CreateAgentRequest do backend: enviar o agente completo;
// AgentService.UpdateAsync preserva Type/Visibility/ProjectId/TenantId do
// existing (não precisa enviar). breakingChange=false (default) trata como
// patch — workflows pinados em ancestors recebem propagação automática.
export interface UpdateAgentFullBody {
  /** Id do agente (precisa bater com o path do PUT — backend valida). */
  id: string
  name: string
  description?: string | null
  model: AgentModel
  provider?: AgentProvider
  instructions?: string | null
  tools?: AgentToolDefinition[]
  middlewares?: AgentMiddleware[]
  structuredOutput?: unknown
  operationalMemory?: unknown
  metadata?: Record<string, string>
  /** Obrigatório no PUT (≥10 chars). Vai pro audit como AdminOverride.Feedback. */
  changeReason: string
  breakingChange?: boolean
}

export const updateAgentFull = (id: string, body: UpdateAgentFullBody) =>
  put<Agent>(`/agents/${id}`, body)

// Versão imutável de um agente publicada via approve em `agent_versions`.
// `revision` é monotônica por agente. `breakingChange` informa se a próxima
// publicação rompe contrato — workflows pinados em ancestors sem breaking
// recebem patch propagation; com breaking ficam presos no snapshot pinado.
export interface AgentVersionSummary {
  agentVersionId: string
  revision: number
  status: string
  createdAt: string
  createdBy?: string | null
  changeReason?: string | null
  contentHash: string
  breakingChange: boolean
}

// Lista versões publicadas do agente em ordem decrescente de revision (mais
// recente primeiro). Alimenta o picker de versão usado nos editores de deploy
// Chat/Routing — backend (`GET /api/aihub/agents/{id}/versions`) responde com
// o snapshot completo, mas a UI usa só revision + breakingChange + createdAt.
export const listAgentVersions = (agentId: string) =>
  get<AgentVersionSummary[]>(`/agents/${agentId}/versions`)
