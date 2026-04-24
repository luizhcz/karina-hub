import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export interface WorkflowEdge {
  from?: string
  to?: string
  edgeType: 'Direct' | 'Conditional' | 'Switch' | 'FanOut' | 'FanIn'
  condition?: string
  targets?: string[]
  cases?: { condition?: string; targets: string[]; isDefault: boolean }[]
  inputSource?: string
}

export interface NodeHitlConfig {
  when: 'before' | 'after'
  interactionType?: 'Approval' | 'Input' | 'Choice'
  prompt: string
  showOutput?: boolean
  options?: string[]
  timeoutSeconds?: number
}

export interface WorkflowExecutorStep {
  id: string
  functionName: string
  description?: string
  hitl?: NodeHitlConfig
}

export interface WorkflowAgentRef {
  agentId: string
  role?: string
  hitl?: NodeHitlConfig
}

export interface WorkflowTrigger {
  type: 'OnDemand' | 'Scheduled' | 'EventDriven'
  cronExpression?: string
  eventTopic?: string
  enabled?: boolean
  disableAfterFire?: boolean
  lastFiredAt?: string
}

export interface WorkflowConfiguration {
  maxRounds?: number
  timeoutSeconds?: number
  enableHumanInTheLoop?: boolean
  checkpointMode?: string
  exposeAsAgent?: boolean
  exposedAgentDescription?: string
  inputMode?: string
  maxHistoryMessages?: number
  maxAgentInvocations?: number
  /** IDs explícitos dos nós que produzem output final (Graph mode). Vazio = auto-detecta via edges. */
  outputNodes?: string[]
}

export interface WorkflowDef {
  id: string
  name: string
  description?: string
  version?: string
  orchestrationMode: string
  agents: WorkflowAgentRef[]
  executors?: WorkflowExecutorStep[]
  edges: WorkflowEdge[]
  trigger?: WorkflowTrigger
  configuration?: WorkflowConfiguration
  metadata?: Record<string, string>
  createdAt?: string
  updatedAt?: string
}

export interface CreateWorkflowRequest {
  id: string
  name: string
  description?: string
  version?: string
  orchestrationMode: string
  agents: WorkflowAgentRef[]
  executors?: WorkflowExecutorStep[]
  edges?: WorkflowEdge[]
  trigger?: WorkflowTrigger
  configuration?: WorkflowConfiguration
  metadata?: Record<string, string>
}

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

export interface WorkflowValidationResult {
  isValid: boolean
  errors: string[]
}

export interface WorkflowDiagram {
  nodes: { id: string; type: string; label: string }[]
  edges: { from: string; to: string; label?: string }[]
}

export interface SandboxResult {
  executionId: string
  mode: string
  statusUrl: string
}

export interface WorkflowVersion {
  versionId: string
  workflowId: string
  createdAt: string
  description?: string
}


export const KEYS = {
  all: ['workflows'] as const,
  visible: ['workflows', 'visible'] as const,
  detail: (id: string) => ['workflows', id] as const,
  executions: (id: string) => ['workflows', id, 'executions'] as const,
  diagram: (id: string) => ['workflows', id, 'diagram'] as const,
  versions: (id: string) => ['workflows', id, 'versions'] as const,
  version: (id: string, vid: string) => ['workflows', id, 'versions', vid] as const,
}


export const getWorkflows = () => get<WorkflowDef[]>('/workflows')
export const getWorkflow = (id: string) => get<WorkflowDef>(`/workflows/${id}`)
export const createWorkflow = (body: CreateWorkflowRequest) => post<WorkflowDef>('/workflows', body)
export const updateWorkflow = (id: string, body: CreateWorkflowRequest) => put<WorkflowDef>(`/workflows/${id}`, body)
export const deleteWorkflow = (id: string) => del(`/workflows/${id}`)
export const triggerWorkflow = (id: string, body: { input: string }) =>
  post<{ executionId: string }>(`/workflows/${id}/trigger`, body)
export const sandboxWorkflow = (id: string, body: { input: string }) =>
  post<SandboxResult>(`/workflows/${id}/sandbox`, body)
export const getWorkflowDiagram = (id: string) => get<WorkflowDiagram>(`/workflows/${id}/diagram`)
export const validateWorkflow = (id: string) => post<WorkflowValidationResult>(`/workflows/${id}/validate`)
export const getWorkflowExecutions = (id: string, params?: { status?: string; pageSize?: number }) =>
  get<WorkflowExecution[]>(`/workflows/${id}/executions`, params)
export const getVisibleWorkflows = () => get<WorkflowDef[]>('/workflows/visible')
export const cloneWorkflow = (id: string, body?: { newId?: string }) => post<WorkflowDef>(`/workflows/${id}/clone`, body)
export const getWorkflowVersions = (id: string) => get<WorkflowVersion[]>(`/workflows/${id}/versions`)
export const getWorkflowVersion = (id: string, vid: string) => get<WorkflowDef>(`/workflows/${id}/versions/${vid}`)
export const rollbackWorkflow = (id: string, body?: { versionId?: string }) => post<WorkflowDef>(`/workflows/${id}/rollback`, body)

export const toggleWorkflowTrigger = (workflow: WorkflowDef, enabled: boolean) => {
  const body = { ...workflow, trigger: workflow.trigger ? { ...workflow.trigger, enabled } : undefined }
  return put<WorkflowDef>(`/workflows/${workflow.id}`, body)
}

export const setWorkflowTrigger = (workflow: WorkflowDef, trigger: WorkflowTrigger | undefined) => {
  const body = { ...workflow, trigger }
  return put<WorkflowDef>(`/workflows/${workflow.id}`, body)
}


export function useWorkflows() {
  return useQuery({ queryKey: KEYS.all, queryFn: getWorkflows })
}

export function useWorkflow(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getWorkflow(id), enabled })
}

export function useVisibleWorkflows() {
  return useQuery({ queryKey: KEYS.visible, queryFn: getVisibleWorkflows })
}

export function useWorkflowDiagram(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.diagram(id), queryFn: () => getWorkflowDiagram(id), enabled })
}

export function useWorkflowExecutions(id: string, params?: { status?: string; pageSize?: number }) {
  return useQuery({
    queryKey: [...KEYS.executions(id), params],
    queryFn: () => getWorkflowExecutions(id, params),
  })
}

export function useWorkflowVersions(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.versions(id), queryFn: () => getWorkflowVersions(id), enabled })
}

export function useWorkflowVersion(id: string, vid: string, enabled = true) {
  return useQuery({ queryKey: KEYS.version(id, vid), queryFn: () => getWorkflowVersion(id, vid), enabled })
}

export function useCreateWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createWorkflow,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useUpdateWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateWorkflowRequest }) => updateWorkflow(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteWorkflow,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useTriggerWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { input: string } }) => triggerWorkflow(id, body),
    onSuccess: (_d, { id }) => { qc.invalidateQueries({ queryKey: KEYS.executions(id) }) },
  })
}

export function useValidateWorkflow() {
  return useMutation({ mutationFn: validateWorkflow })
}

export function useSandboxWorkflow() {
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { input: string } }) => sandboxWorkflow(id, body),
  })
}

export function useCloneWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body?: { newId?: string } }) => cloneWorkflow(id, body),
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useRollbackWorkflow() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body?: { versionId?: string } }) => rollbackWorkflow(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: KEYS.versions(id) })
    },
  })
}

export function useToggleWorkflowTrigger() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ workflow, enabled }: { workflow: WorkflowDef; enabled: boolean }) =>
      toggleWorkflowTrigger(workflow, enabled),
    onSuccess: (_d, { workflow }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(workflow.id) })
    },
  })
}

export function useSetWorkflowTrigger() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ workflow, trigger }: { workflow: WorkflowDef; trigger: WorkflowTrigger | undefined }) =>
      setWorkflowTrigger(workflow, trigger),
    onSuccess: (_d, { workflow }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(workflow.id) })
    },
  })
}
