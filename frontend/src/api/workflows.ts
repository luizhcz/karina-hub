import { get, post, put, patch, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export type EdgeOperator =
  | 'Eq'
  | 'NotEq'
  | 'Gt'
  | 'Gte'
  | 'Lt'
  | 'Lte'
  | 'Contains'
  | 'StartsWith'
  | 'EndsWith'
  | 'MatchesRegex'
  | 'In'
  | 'NotIn'
  | 'IsNull'
  | 'IsNotNull'

export type EdgePredicateValueType =
  | 'Auto'
  | 'String'
  | 'Number'
  | 'Integer'
  | 'Boolean'
  | 'Enum'

export interface EdgePredicate {
  /** JSONPath subset: $, $.field, $.a.b, $.list[N], $.results[0].status. */
  path: string
  operator: EdgeOperator
  /** Preserva tipo (number ≠ string-de-number). Null em IsNull/IsNotNull. Array em In/NotIn. */
  value?: unknown
  valueType: EdgePredicateValueType
  /** sha256 curto do schema do produtor no momento da criação — invalida em schema drift. */
  sourceSchemaVersion?: string
}

export interface WorkflowSwitchCase {
  predicate?: EdgePredicate
  targets: string[]
  isDefault: boolean
}

export interface WorkflowEdge {
  from?: string
  to?: string
  edgeType: 'Direct' | 'Conditional' | 'Switch' | 'FanOut' | 'FanIn'
  /** Conditional: predicate tipado avaliado sobre o output JSON do produtor. */
  predicate?: EdgePredicate
  /** Switch: cases avaliados em ordem (primeiro match vence) + opcional default. */
  cases?: WorkflowSwitchCase[]
  /** FanOut/FanIn: targets/sources. */
  targets?: string[]
  sources?: string[]
  /** Hint textual em modo Handoff — metadata pro orquestrador, não predicate. */
  handoffHint?: string
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

export type WorkflowVisibility = 'project' | 'global'

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
  /** "project" (default) | "global" — global é visível a todos os projetos do tenant. */
  visibility?: WorkflowVisibility
  /** Project owner do workflow. Distingue de quem está consumindo. */
  originProjectId?: string
  /** Tenant do owner. */
  originTenantId?: string
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

/** Mudança individual entre pinned e current — usada pelo diff modal. */
export interface WorkflowAgentVersionChangeEntry {
  agentVersionId: string
  revision: number
  /** true=breaking, false=patch, null=legacy/unknown (tratado como breaking pelo resolver). */
  breakingChange?: boolean | null
  changeReason?: string | null
  createdAt: string
  createdBy?: string | null
}

/** Status consolidado de um agent ref dentro de um workflow. */
export interface WorkflowAgentVersionStatus {
  agentId: string
  agentName?: string | null
  pinnedVersionId?: string | null
  pinnedRevision?: number | null
  currentVersionId?: string | null
  currentRevision?: number | null
  /** true se há AgentVersion com BreakingChange=true entre pinned e current. */
  isPinnedBlockedByBreaking: boolean
  /** true quando há current.Revision > pinned.Revision (UI mostra badge). */
  hasUpdate: boolean
  /** Versions intermediárias entre pinned e current (ordenadas por revision ASC). */
  changes: WorkflowAgentVersionChangeEntry[]
}

export const KEYS = {
  all: ['workflows'] as const,
  visible: ['workflows', 'visible'] as const,
  detail: (id: string) => ['workflows', id] as const,
  executions: (id: string) => ['workflows', id, 'executions'] as const,
  diagram: (id: string) => ['workflows', id, 'diagram'] as const,
  versions: (id: string) => ['workflows', id, 'versions'] as const,
  version: (id: string, vid: string) => ['workflows', id, 'versions', vid] as const,
  agentVersionStatus: (id: string) => ['workflows', id, 'agent-version-status'] as const,
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
export const updateWorkflowVisibility = (id: string, body: { visibility: WorkflowVisibility; reason?: string }) =>
  patch<WorkflowDef>(`/workflows/${id}/visibility`, body)

export const getWorkflowAgentVersionStatus = (id: string) =>
  get<WorkflowAgentVersionStatus[]>(`/workflows/${id}/agent-version-status`)

export const updateWorkflowAgentPin = (
  workflowId: string,
  agentId: string,
  body: { newVersionId: string; reason?: string },
) => patch<WorkflowDef>(`/workflows/${workflowId}/agents/${agentId}/pin`, body)

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

export function useWorkflowAgentVersionStatus(workflowId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.agentVersionStatus(workflowId),
    queryFn: () => getWorkflowAgentVersionStatus(workflowId),
    enabled,
  })
}

export function useUpdateWorkflowAgentPin() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ workflowId, agentId, body }: {
      workflowId: string
      agentId: string
      body: { newVersionId: string; reason?: string }
    }) => updateWorkflowAgentPin(workflowId, agentId, body),
    onSuccess: (_d, { workflowId }) => {
      qc.invalidateQueries({ queryKey: KEYS.detail(workflowId) })
      qc.invalidateQueries({ queryKey: KEYS.agentVersionStatus(workflowId) })
    },
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

export function useUpdateWorkflowVisibility() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, visibility, reason }: { id: string; visibility: WorkflowVisibility; reason?: string }) =>
      updateWorkflowVisibility(id, { visibility, reason }),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.visible })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
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
