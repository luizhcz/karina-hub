import { del, get, post } from './client'

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
