import { get } from './client'
import { useQuery } from '@tanstack/react-query'


export interface LlmTokenUsage {
  id: number
  agentId: string
  modelId: string
  executionId?: string
  workflowId?: string
  inputTokens: number
  outputTokens: number
  totalTokens: number
  durationMs: number
  promptVersionId?: string
  outputContent?: string
  createdAt: string
}

export interface AgentTokenSummary {
  agentId: string
  modelId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
}

export interface GlobalTokenSummary {
  totalInput: number
  totalOutput: number
  totalTokens: number
  totalCalls: number
  avgDurationMs: number
  byAgent: AgentTokenSummary[]
}

export interface ThroughputBucket {
  bucket: string
  executions: number
  tokens: number
  llmCalls: number
  avgDurationMs: number
}

export interface ThroughputResult {
  buckets: ThroughputBucket[]
  avgExecutionsPerHour: number
  avgTokensPerHour: number
  avgCallsPerHour: number
}

export interface WorkflowTokenSummary {
  workflowId: string
  modelId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
}

export interface ProjectTokenSummary {
  projectId: string
  modelId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
}

export interface DateRangeParams {
  from?: string
  to?: string
}


export const KEYS = {
  summary: (params?: DateRangeParams) => ['token-usage', 'summary', params] as const,
  execution: (id: string) => ['token-usage', 'execution', id] as const,
  throughput: (params?: DateRangeParams) => ['token-usage', 'throughput', params] as const,
  agentSummary: (id: string, params?: DateRangeParams) => ['token-usage', 'agent', id, 'summary', params] as const,
  agentHistory: (id: string) => ['token-usage', 'agent', id, 'history'] as const,
  workflowsSummary: (params?: DateRangeParams) => ['token-usage', 'workflows', 'summary', params] as const,
  projectsSummary: (params?: DateRangeParams) => ['token-usage', 'projects', 'summary', params] as const,
}


export const getTokenSummary = (params?: DateRangeParams) =>
  get<GlobalTokenSummary>('/token-usage/summary', params)

export const getTokensByExecution = (executionId: string) =>
  get<LlmTokenUsage[]>(`/token-usage/executions/${executionId}`)

export const getThroughput = (params?: DateRangeParams) =>
  get<ThroughputResult>('/token-usage/throughput', params)

export const getAgentTokenSummary = (agentId: string, params?: DateRangeParams) =>
  get<AgentTokenSummary>(`/token-usage/agents/${agentId}/summary`, params)

export const getAgentTokenHistory = (agentId: string, limit = 100) =>
  get<LlmTokenUsage[]>(`/token-usage/agents/${agentId}/history`, { limit })

export const getWorkflowsSummary = (params?: DateRangeParams) =>
  get<WorkflowTokenSummary[]>('/token-usage/workflows/summary', params)

export const getProjectsSummary = (params?: DateRangeParams) =>
  get<ProjectTokenSummary[]>('/token-usage/projects/summary', params)


export function useTokenSummary(params?: DateRangeParams) {
  return useQuery({
    queryKey: KEYS.summary(params),
    queryFn: () => getTokenSummary(params),
  })
}

export function useTokensByExecution(executionId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.execution(executionId),
    queryFn: () => getTokensByExecution(executionId),
    enabled,
  })
}

export function useThroughput(params?: DateRangeParams) {
  return useQuery({
    queryKey: KEYS.throughput(params),
    queryFn: () => getThroughput(params),
  })
}

export function useAgentTokenSummary(agentId: string, params?: DateRangeParams) {
  return useQuery({
    queryKey: KEYS.agentSummary(agentId, params),
    queryFn: () => getAgentTokenSummary(agentId, params),
    enabled: !!agentId,
  })
}

export function useAgentTokenHistory(agentId: string, limit = 100) {
  return useQuery({
    queryKey: KEYS.agentHistory(agentId),
    queryFn: () => getAgentTokenHistory(agentId, limit),
    enabled: !!agentId,
  })
}

export function useWorkflowsSummary(params?: DateRangeParams) {
  return useQuery({
    queryKey: KEYS.workflowsSummary(params),
    queryFn: () => getWorkflowsSummary(params),
  })
}

export function useProjectsSummary(params?: DateRangeParams) {
  return useQuery({
    queryKey: KEYS.projectsSummary(params),
    queryFn: () => getProjectsSummary(params),
  })
}
