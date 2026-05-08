import { get, post, put } from './client'

export type OrchestrationMode =
  | 'Sequential'
  | 'Concurrent'
  | 'Handoff'
  | 'GroupChat'
  | 'Graph'

export interface WorkflowAgentReference {
  agentId: string
  role?: string | null
  agentVersionId?: string | null
}

export interface WorkflowConfiguration {
  maxRounds?: number
  timeoutSeconds?: number
  checkpointMode?: 'InMemory' | 'Postgres' | 'Blob'
  inputMode?: 'Standalone' | 'Chat'
  enableHumanInTheLoop?: boolean
  exposeAsAgent?: boolean
  maxHistoryMessages?: number
  maxAgentInvocations?: number
  maxTokensPerExecution?: number
}

export interface CreateWorkflowBody {
  id: string
  name: string
  description?: string | null
  version?: string
  orchestrationMode: OrchestrationMode
  agents: WorkflowAgentReference[]
  executors?: unknown[]
  edges?: unknown[]
  configuration?: WorkflowConfiguration
  metadata?: Record<string, string>
  visibility?: 'project' | 'global'
}

export interface Workflow {
  id: string
  name: string
  description?: string | null
  version: string
  orchestrationMode: OrchestrationMode
  agents: WorkflowAgentReference[]
  configuration: WorkflowConfiguration
  visibility: string
  originProjectId?: string | null
  originTenantId?: string | null
  createdAt: string
  updatedAt: string
  // Snapshot ativo em runtime — calculado pelo backend matchando ContentHash do
  // estado mutável atual contra workflow_versions. Nullable em casos patológicos.
  currentVersionId?: string | null
  currentRevision?: number | null
  [key: string]: unknown
}

/**
 * @param scope `'project'` retorna apenas workflows do ProjectId atual. Sem o param,
 *  inclui também `Visibility=global` de outros projetos do mesmo tenant.
 */
export const listWorkflows = (scope?: 'project') => {
  const qs = scope ? `?scope=${scope}` : ''
  return get<Workflow[]>(`/workflows${qs}`)
}

export const getWorkflow = (id: string) => get<Workflow>(`/workflows/${id}`)

export const createWorkflow = (body: CreateWorkflowBody) =>
  post<Workflow>('/workflows', body)

// Heurística de UI: workflow gerado pela tela de Implantações tem id no formato
// `deploy-<agentId>` E metadata.deployedFromAgentId=<agentId>. Workflows criados
// por outras vias (admin via API) ficam fora da lista — a tela só lista o que
// nasceu do fluxo de implantação.
export function isAgentDeployment(workflow: Workflow): boolean {
  if (!workflow.id.startsWith('deploy-')) return false
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return !!md?.deployedFromAgentId
}

export function deployedAgentId(workflow: Workflow): string | null {
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.deployedFromAgentId ?? null
}

export interface WorkflowVersion {
  workflowVersionId: string
  workflowDefinitionId: string
  revision: number
  status: string
  createdAt: string
  createdBy?: string | null
  changeReason?: string | null
  contentHash: string
}

export const listWorkflowVersions = (workflowId: string) =>
  get<WorkflowVersion[]>(`/workflows/${workflowId}/versions`)

export const getWorkflowVersion = (workflowId: string, versionId: string) =>
  get<WorkflowVersion>(`/workflows/${workflowId}/versions/${versionId}`)

// Body é { versionId } — backend usa esse mesmo campo (não targetVersionId).
export const rollbackWorkflow = (workflowId: string, versionId: string) =>
  post<Workflow>(`/workflows/${workflowId}/rollback`, { versionId })

export interface WorkflowEnabledStatus {
  enabled: boolean
  totalAgents: number
  enabledAgents: number
  agents: { agentId: string; enabled: boolean | null }[]
}

export const getWorkflowEnabledStatus = (workflowId: string) =>
  get<WorkflowEnabledStatus>(`/workflows/${workflowId}/enabled-status`)

// Reimplantar = PUT do workflow com agentVersionId pinado na revision atual do
// agente. Útil pra criar uma nova revision do workflow vinculada a uma versão
// específica do agente (governança: "deploy de hoje usa agent v5"). Sem o pin,
// o ContentHash não muda e o re-PUT é no-op idempotente.
export const updateWorkflow = (id: string, body: CreateWorkflowBody) =>
  put<Workflow>(`/workflows/${id}`, body)

// Convenção: id determinístico = "deploy-<agentId>". Permite idempotência —
// telas que revisitam o mesmo agente recuperam o workflow existente via GET
// sem criar duplicado.
export const deploymentWorkflowId = (agentId: string) => `deploy-${agentId}`
