import { get, patch, post } from './client'
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
