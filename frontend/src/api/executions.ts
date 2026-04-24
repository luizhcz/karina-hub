import { get, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export interface WorkflowExecution {
  executionId: string
  workflowId: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Paused'
  input?: string
  output?: string
  errorMessage?: string
  startedAt: string
  completedAt?: string
  metadata?: Record<string, string>
}

export interface ExecutionPage {
  items: WorkflowExecution[]
  total: number
  page: number
  pageSize: number
}

export interface NodeRecord {
  nodeId: string
  executionId: string
  nodeType: 'agent' | 'executor' | 'trigger'
  status: 'pending' | 'running' | 'completed' | 'failed'
  startedAt?: string
  completedAt?: string
  output?: string
  iteration: number
  tokensUsed?: number
}

export interface ToolInvocation {
  id: number
  executionId: string
  agentId: string
  toolName: string
  arguments?: string
  result?: string
  durationMs: number
  success: boolean
  errorMessage?: string
  createdAt: string
}

export interface ExecutionEventRecord {
  eventType: string
  executionId: string
  payload: string
  timestamp: string
}

export interface ExecutionFull {
  execution: WorkflowExecution
  nodes: NodeRecord[]
  events: ExecutionEventRecord[]
  tools: ToolInvocation[]
}

export interface ExecutionListParams {
  workflowId?: string
  status?: string
  from?: string
  to?: string
  page?: number
  pageSize?: number
}


export const KEYS = {
  all: ['executions'] as const,
  list: (params?: ExecutionListParams) => ['executions', 'list', params] as const,
  detail: (id: string) => ['executions', id] as const,
  nodes: (id: string) => ['executions', id, 'nodes'] as const,
  tools: (id: string) => ['executions', id, 'tools'] as const,
  events: (id: string) => ['executions', id, 'events'] as const,
  full: (id: string) => ['executions', id, 'full'] as const,
}


export const getExecutions = (params?: ExecutionListParams) =>
  get<ExecutionPage>('/executions', params as Record<string, string | number | undefined>)

export const getExecution = (id: string) => get<WorkflowExecution>(`/executions/${id}`)
export const cancelExecution = (id: string) => del(`/executions/${id}`)
export const getExecutionNodes = (id: string) => get<NodeRecord[]>(`/executions/${id}/nodes`)
export const getExecutionTools = (id: string) => get<ToolInvocation[]>(`/executions/${id}/tools`)
export const getExecutionEvents = (id: string) => get<ExecutionEventRecord[]>(`/executions/${id}/events`)
export const getExecutionFull = (id: string) => get<ExecutionFull>(`/executions/${id}/full`)


export function useExecutions(params?: ExecutionListParams) {
  return useQuery({
    queryKey: KEYS.list(params),
    queryFn: () => getExecutions(params),
  })
}

export function useExecution(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getExecution(id), enabled })
}

export function useExecutionNodes(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.nodes(id), queryFn: () => getExecutionNodes(id), enabled })
}

export function useExecutionTools(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.tools(id), queryFn: () => getExecutionTools(id), enabled })
}

export function useExecutionEvents(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.events(id), queryFn: () => getExecutionEvents(id), enabled })
}

export function useExecutionFull(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.full(id), queryFn: () => getExecutionFull(id), enabled })
}

export function useCancelExecution() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: cancelExecution,
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}
