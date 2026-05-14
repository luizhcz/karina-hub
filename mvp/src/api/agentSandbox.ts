import { del, get, post } from './client'

/**
 * Chaves canônicas do <code>workflow.metadata</code> escritas pelo
 * AgentSandboxService e lidas pelo frontend pra distinguir sandbox de deploy
 * real. Espelho 1-1 das constantes em <code>AgentSandboxMetadata</code> no
 * backend (.NET). <code>SessionId</code> mantém o nome legado
 * <code>chatSandboxSessionId</code> pra preservar leitores existentes — rename
 * de chave entra junto com migração coordenada FE/BE.
 */
export const AgentSandboxMetadataKeys = {
  SessionId: 'chatSandboxSessionId',
  Kind: 'kind',
  KindChatSandbox: 'chat-sandbox',
  KindStandaloneSandbox: 'standalone-sandbox',
} as const

/**
 * API unificada de Agent Sandbox. Backend decide o <code>mode</code> por
 * <code>agent.Type</code>:
 *
 * <ul>
 *   <li><b>chat</b> — Conversational. Cria workflow Chat efêmero + conversation;
 *       frontend renderiza via <code>ChatDeploymentSandbox</code>.</li>
 *   <li><b>standalone</b> — Custom/Worker/ToolRunner. Cria workflow Standalone
 *       single-shot sem conversation; frontend renderiza via
 *       <code>DeploymentSandbox</code>.</li>
 * </ul>
 *
 * Router não usa este fluxo — endpoint dedicado <code>/predict-intent</code>
 * faz a classificação stateless. Tentar criar sandbox de Router retorna 400.
 */
export type AgentSandboxMode = 'chat' | 'standalone'

export type AgentSandboxSessionStatus = 'Active' | 'Validated' | 'Expired' | 'Closed'

export interface AgentSandboxSession {
  sessionId: string
  agentId: string
  agentVersionId: string
  mode: AgentSandboxMode
  workflowId: string
  conversationId: string | null
  projectId: string
  createdByUserId: string
  createdAt: string
  lastMessageAt?: string | null
  expiresAt: string
  status: AgentSandboxSessionStatus
  validatedAt?: string | null
  validatedByUserId?: string | null
  validationNotes?: string | null
}

export interface AgentSandboxCreateBody {
  agentVersionId?: string | null
}

export interface AgentSandboxValidateBody {
  notes?: string | null
}

export const createAgentSandboxSession = (agentId: string, body: AgentSandboxCreateBody = {}) =>
  post<AgentSandboxSession>(`/agents/${agentId}/sandbox-sessions`, body)

export const listAgentSandboxSessions = (agentId: string, statusFilter?: AgentSandboxSessionStatus) => {
  const qs = statusFilter ? `?status=${statusFilter}` : ''
  return get<AgentSandboxSession[]>(`/agents/${agentId}/sandbox-sessions${qs}`)
}

export const getAgentSandboxSession = (sessionId: string) =>
  get<AgentSandboxSession>(`/sandbox-sessions/${sessionId}`)

export const closeAgentSandboxSession = (sessionId: string) =>
  del<void>(`/sandbox-sessions/${sessionId}`)

export const validateAgentSandboxSession = (sessionId: string, body: AgentSandboxValidateBody = {}) =>
  post<AgentSandboxSession>(`/sandbox-sessions/${sessionId}/validate`, body)

/**
 * Predict-only de Router: classifica o input via LLM e devolve intent +
 * reasoning sem criar session. Backend rejeita (400) se agent não é Router.
 */
export interface RouterPredictBody {
  input: string
  agentVersionId?: string | null
}

export interface RouterPredictResult {
  intent: string
  reasoning: string | null
  rawOutput: string
  latencyMs: number
  agentVersionId: string
}

export const predictRouterIntent = (agentId: string, body: RouterPredictBody) =>
  post<RouterPredictResult>(`/agents/${agentId}/predict-intent`, body)
