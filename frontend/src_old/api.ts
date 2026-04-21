import type { AgentDef, CreateAgentRequest, CreateWorkflowRequest, AvailableFunctions, WorkflowDef, WorkflowExecution, NodeRecord, ExecutionEventRecord, ExecutionSummary, ExecutionTimeseries, HumanInteraction, ModelPricing, CreateModelPricingRequest } from './types'

const BASE = '/api'

const safeFetch = <T>(url: string, init?: RequestInit): Promise<T> =>
  fetch(url, init).then(r => {
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    return r.json() as Promise<T>
  })

export const api = {
  getAgent: (id: string): Promise<AgentDef> =>
    safeFetch<AgentDef>(`${BASE}/agents/${id}`),

  getAgents: (): Promise<AgentDef[]> =>
    safeFetch<AgentDef[]>(`${BASE}/agents`).catch(e => { console.warn('[api] getAgents failed:', e); return [] }),

  createAgent: (body: CreateAgentRequest): Promise<AgentDef> =>
    safeFetch<AgentDef>(`${BASE}/agents`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  updateAgent: (id: string, body: CreateAgentRequest): Promise<AgentDef> =>
    safeFetch<AgentDef>(`${BASE}/agents/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  deleteAgent: (id: string): Promise<void> =>
    fetch(`${BASE}/agents/${id}`, { method: 'DELETE' }).then(r => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
    }),

  validateAgent: (id: string): Promise<{ isValid: boolean; errors: string[] }> =>
    safeFetch<{ isValid: boolean; errors: string[] }>(`${BASE}/agents/${id}/validate`, {
      method: 'POST',
    }),

  getFunctions: (): Promise<AvailableFunctions> =>
    safeFetch<AvailableFunctions>(`${BASE}/functions`).catch(e => { console.warn('[api] getFunctions failed:', e); return { functionTools: [], codeExecutors: [] } }),

  getWorkflows: (): Promise<WorkflowDef[]> =>
    safeFetch<WorkflowDef[]>(`${BASE}/workflows`).catch(e => { console.warn('[api] getWorkflows failed:', e); return [] }),

  getWorkflow: (id: string): Promise<WorkflowDef> =>
    safeFetch<WorkflowDef>(`${BASE}/workflows/${id}`),

  createWorkflow: (body: CreateWorkflowRequest): Promise<WorkflowDef> =>
    safeFetch<WorkflowDef>(`${BASE}/workflows`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  updateWorkflow: (id: string, body: CreateWorkflowRequest): Promise<WorkflowDef> =>
    safeFetch<WorkflowDef>(`${BASE}/workflows/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  toggleWorkflowTrigger: (workflow: WorkflowDef, enabled: boolean): Promise<WorkflowDef> => {
    const body = { ...workflow, trigger: workflow.trigger ? { ...workflow.trigger, enabled } : undefined }
    return safeFetch<WorkflowDef>(`${BASE}/workflows/${workflow.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  },

  setWorkflowTrigger: (workflow: WorkflowDef, trigger: import('./types').WorkflowTrigger | undefined): Promise<WorkflowDef> => {
    const body = { ...workflow, trigger }
    return safeFetch<WorkflowDef>(`${BASE}/workflows/${workflow.id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    })
  },

  getWorkflowExecutions: (workflowId: string, status?: string, pageSize = 20): Promise<WorkflowExecution[]> =>
    safeFetch<WorkflowExecution[]>(
      `${BASE}/workflows/${workflowId}/executions?pageSize=${pageSize}${status ? `&status=${status}` : ''}`
    ).catch(e => { console.warn('[api] getWorkflowExecutions failed:', e); return [] }),

  getAllExecutions: (params?: {
    workflowId?: string
    status?: string
    from?: string
    to?: string
    page?: number
    pageSize?: number
  }): Promise<{ items: WorkflowExecution[]; total: number; page: number; pageSize: number }> => {
    const q = new URLSearchParams()
    if (params?.workflowId) q.set('workflowId', params.workflowId)
    if (params?.status)     q.set('status', params.status)
    if (params?.from)       q.set('from', params.from)
    if (params?.to)         q.set('to', params.to)
    if (params?.page)       q.set('page', String(params.page))
    if (params?.pageSize)   q.set('pageSize', String(params.pageSize))
    return safeFetch<{ items: WorkflowExecution[]; total: number; page: number; pageSize: number }>(
      `${BASE}/executions?${q}`
    ).catch(e => { console.warn('[api] getAllExecutions failed:', e); return { items: [], total: 0, page: 1, pageSize: 50 } })
  },

  cancelExecution: (id: string): Promise<void> =>
    fetch(`${BASE}/executions/${id}`, { method: 'DELETE' }).then(r => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
    }),

  getExecution: (id: string): Promise<WorkflowExecution> =>
    safeFetch<WorkflowExecution>(`${BASE}/executions/${id}`),

  getNodes: (executionId: string): Promise<NodeRecord[]> =>
    safeFetch<NodeRecord[]>(`${BASE}/executions/${executionId}/nodes`).catch(e => { console.warn('[api] getNodes failed:', e); return [] }),

  getExecutionEvents: (executionId: string): Promise<ExecutionEventRecord[]> =>
    safeFetch<ExecutionEventRecord[]>(`${BASE}/executions/${executionId}/events`).catch(e => { console.warn('[api] getExecutionEvents failed:', e); return [] }),

  triggerWorkflow: (workflowId: string, input: string): Promise<{ executionId: string }> =>
    safeFetch<{ executionId: string }>(`${BASE}/workflows/${workflowId}/trigger`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ input }),
    }),
}

// ── Chat API ──────────────────────────────────────────────────────────────────
import type { ConversationSession, ChatMsg, ChatSendResult } from './types'

type UserType = 'cliente' | 'assessor'

const userHeader = (userId: string, userType: UserType): Record<string, string> =>
  userType === 'cliente'
    ? { 'x-efs-account': userId }
    : { 'x-efs-user-profile-id': userId }

export const chatApi = {
  createConversation: (
    userId: string,
    userType: UserType,
    workflowId?: string,
    metadata?: Record<string, string>
  ): Promise<ConversationSession> =>
    safeFetch<ConversationSession>(`${BASE}/conversations`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...userHeader(userId, userType),
      },
      body: JSON.stringify({ workflowId: workflowId || undefined, metadata }),
    }),

  getConversation: (id: string, userId: string, userType: UserType = 'cliente'): Promise<ConversationSession> =>
    safeFetch<ConversationSession>(`${BASE}/conversations/${id}`, {
      headers: userHeader(userId, userType),
    }),

  getMessages: (id: string, limit = 50, offset = 0): Promise<ChatMsg[]> =>
    safeFetch<ChatMsg[]>(`${BASE}/conversations/${id}/messages?limit=${limit}&offset=${offset}`),

  sendMessage: (
    id: string,
    userId: string,
    userType: UserType,
    messages: { role: string; message: string }[]
  ): Promise<ChatSendResult> =>
    safeFetch<ChatSendResult>(`${BASE}/conversations/${id}/messages`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...userHeader(userId, userType),
      },
      body: JSON.stringify(messages),
    }),

  openStream: (id: string): EventSource =>
    new EventSource(`${BASE}/conversations/${id}/messages/stream`),

  getUserConversations: (userId: string): Promise<ConversationSession[]> =>
    safeFetch<ConversationSession[]>(`${BASE}/users/${userId}/conversations`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  clearContext: (id: string): Promise<void> =>
    safeFetch<void>(`${BASE}/conversations/${id}/context`, { method: 'DELETE' }),

  deleteConversation: (id: string): Promise<void> =>
    fetch(`${BASE}/conversations/${id}`, { method: 'DELETE' }).then(r => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
    }),
}

// ── Prompt Versioning API ────────────────────────────────────────────────────
import type { AgentPromptVersion } from './types'

export const promptApi = {
  listVersions: (agentId: string): Promise<AgentPromptVersion[]> =>
    safeFetch<AgentPromptVersion[]>(`${BASE}/agents/${agentId}/prompts`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  saveVersion: (agentId: string, versionId: string, content: string): Promise<{ agentId: string; versionId: string }> =>
    safeFetch<{ agentId: string; versionId: string }>(`${BASE}/agents/${agentId}/prompts`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ versionId, content }),
    }),

  setMaster: (agentId: string, versionId: string): Promise<{ agentId: string; master: string }> =>
    safeFetch<{ agentId: string; master: string }>(`${BASE}/agents/${agentId}/prompts/master`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ versionId }),
    }),

  deleteVersion: (agentId: string, versionId: string): Promise<void> =>
    fetch(`${BASE}/agents/${agentId}/prompts/${encodeURIComponent(versionId)}`, { method: 'DELETE' })
      .then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`) }),
}

// ── Tool Invocations API ─────────────────────────────────────────────────────
import type { ToolInvocation } from './types'

export const toolsApi = {
  getByExecution: (executionId: string): Promise<ToolInvocation[]> =>
    safeFetch<ToolInvocation[]>(`${BASE}/executions/${executionId}/tools`).catch(e => { console.warn('[api] request failed:', e); return [] }),
}

// ── Model Pricing API ────────────────────────────────────────────────────────

export const pricingApi = {
  getAll: (): Promise<ModelPricing[]> =>
    safeFetch<ModelPricing[]>(`${BASE}/admin/model-pricing`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  create: (body: CreateModelPricingRequest): Promise<ModelPricing> =>
    safeFetch<ModelPricing>(`${BASE}/admin/model-pricing`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),

  delete: (id: number): Promise<void> =>
    fetch(`${BASE}/admin/model-pricing/${id}`, { method: 'DELETE' }).then(r => {
      if (!r.ok) throw new Error(`HTTP ${r.status}`)
    }),
}

// ── HITL Interactions API ─────────────────────────────────────────────────────

export const interactionsApi = {
  getPending: (): Promise<HumanInteraction[]> =>
    safeFetch<HumanInteraction[]>(`${BASE}/interactions/pending`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  getByExecution: (executionId: string): Promise<HumanInteraction[]> =>
    safeFetch<HumanInteraction[]>(`${BASE}/interactions/by-execution/${executionId}`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  resolve: (id: string, response: string, approved: boolean): Promise<void> =>
    fetch(`${BASE}/interactions/${id}/resolve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ response, approved }),
    }).then(r => { if (!r.ok) throw new Error(`HTTP ${r.status}`) }),
}

// ── Analytics API ─────────────────────────────────────────────────────────────

export const analyticsApi = {
  getSummary: (from?: string, to?: string, workflowId?: string): Promise<ExecutionSummary> => {
    const q = new URLSearchParams()
    if (from) q.set('from', from)
    if (to) q.set('to', to)
    if (workflowId) q.set('workflowId', workflowId)
    return safeFetch<ExecutionSummary>(`${BASE}/analytics/executions/summary?${q}`)
  },

  getTimeseries: (from?: string, to?: string, workflowId?: string, groupBy?: string): Promise<ExecutionTimeseries> => {
    const q = new URLSearchParams()
    if (from) q.set('from', from)
    if (to) q.set('to', to)
    if (workflowId) q.set('workflowId', workflowId)
    if (groupBy) q.set('groupBy', groupBy)
    return safeFetch<ExecutionTimeseries>(`${BASE}/analytics/executions/timeseries?${q}`)
  },
}

// ── Token Usage API ──────────────────────────────────────────────────────────
import type { GlobalTokenSummary, LlmTokenUsage, ThroughputResult, AgentTokenSummary } from './types'

export const tokenApi = {
  getSummary: (from?: string, to?: string): Promise<GlobalTokenSummary> => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    return safeFetch<GlobalTokenSummary>(`${BASE}/token-usage/summary?${params}`)
  },

  getByExecution: (executionId: string): Promise<LlmTokenUsage[]> =>
    safeFetch<LlmTokenUsage[]>(`${BASE}/token-usage/executions/${executionId}`).catch(e => { console.warn('[api] request failed:', e); return [] }),

  getThroughput: (from?: string, to?: string): Promise<ThroughputResult> => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    return safeFetch<ThroughputResult>(`${BASE}/token-usage/throughput?${params}`)
  },

  getAgentSummary: (agentId: string, from?: string, to?: string): Promise<AgentTokenSummary> => {
    const params = new URLSearchParams()
    if (from) params.set('from', from)
    if (to) params.set('to', to)
    return safeFetch<AgentTokenSummary>(`${BASE}/token-usage/agents/${agentId}/summary?${params}`)
  },

  getAgentHistory: (agentId: string, limit = 100): Promise<LlmTokenUsage[]> =>
    safeFetch<LlmTokenUsage[]>(`${BASE}/token-usage/agents/${agentId}/history?limit=${limit}`).catch(e => { console.warn('[api] request failed:', e); return [] }),
}

// ── Agent Sessions API (Playground) ──────────────────────────────────────────
export const sessionApi = {
  create: (agentId: string): Promise<{ sessionId: string; agentId: string; turnCount: number }> =>
    safeFetch<{ sessionId: string; agentId: string; turnCount: number }>(`${BASE}/agents/${agentId}/sessions`, { method: 'POST' }),

  run: (agentId: string, sessionId: string, message: string): Promise<{ sessionId: string; response: string; turnCount: number }> =>
    safeFetch<{ sessionId: string; response: string; turnCount: number }>(`${BASE}/agents/${agentId}/sessions/${sessionId}/run`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message }),
    }),

  openStream: (agentId: string, sessionId: string, message: string): { source: Promise<Response>; body: ReadableStream<Uint8Array> | null } => {
    const source = fetch(`${BASE}/agents/${agentId}/sessions/${sessionId}/stream`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ message }),
    })
    return { source, body: null }
  },

  delete: (agentId: string, sessionId: string): Promise<void> =>
    fetch(`${BASE}/agents/${agentId}/sessions/${sessionId}`, { method: 'DELETE' }).then(r => {
      if (!r.ok && r.status !== 404) throw new Error(`HTTP ${r.status}`)
    }),
}

// ── Admin API ─────────────────────────────────────────────────────────────────

export const adminApi = {
  getConversations: (params?: {
    userId?: string
    workflowId?: string
    from?: string
    to?: string
    page?: number
    pageSize?: number
  }): Promise<{ items: ConversationSession[]; total: number; page: number; pageSize: number }> => {
    const q = new URLSearchParams()
    if (params?.userId)     q.set('userId', params.userId)
    if (params?.workflowId) q.set('workflowId', params.workflowId)
    if (params?.from)       q.set('from', params.from)
    if (params?.to)         q.set('to', params.to)
    if (params?.page)       q.set('page', String(params.page))
    if (params?.pageSize)   q.set('pageSize', String(params.pageSize))
    return safeFetch<{ items: ConversationSession[]; total: number; page: number; pageSize: number }>(`${BASE}/admin/conversations?${q}`)
      .catch(e => { console.warn('[api] adminApi.getConversations failed:', e); return { items: [], total: 0, page: 1, pageSize: 50 } })
  },
}
