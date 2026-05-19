import { generateDraftId } from './agentDrafts'
import { API_BASE_URL } from './baseUrl'
import { get, post, put } from './client'

// Marca workflows criados pelo editor de pipeline (sequência de N agentes) pra
// distinguir de single-agent deploys na listagem. Backend trata metadata como
// dicionário opaco — esse marcador é só convenção do MVP.
export const PIPELINE_DEPLOYMENT_KIND = 'pipeline'
export const ROUTING_DEPLOYMENT_KIND = 'routing'
export const CHAT_DEPLOYMENT_KIND = 'chat'

// Valores de metadata.kind que marcam workflows efêmeros criados pelo
// AgentSandboxService quando o user clica "Testar". Eles compartilham id
// prefix (`deploy-chat-sandbox-…`) e metadata.deploymentKind=chat com deploys
// de produção — a única forma de distinguir é via metadata.kind.
const CHAT_SANDBOX_KIND = 'chat-sandbox'
const STANDALONE_SANDBOX_KIND = 'standalone-sandbox'

function isSandboxWorkflow(workflow: Workflow): boolean {
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  const kind = md?.kind
  return kind === CHAT_SANDBOX_KIND || kind === STANDALONE_SANDBOX_KIND
}

export const pipelineWorkflowId = () => `deploy-pipeline-${generateDraftId()}`
export const routingWorkflowId = () => `deploy-routing-${generateDraftId()}`
export const chatWorkflowId = () => `deploy-chat-${generateDraftId()}`

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

/**
 * Rótulo canônico de revisão exibido na UI. Mantém prefixo único em todos os
 * pontos (chips, selectors, warnings) — drift entre telas confunde PM/PO que
 * comparam versões em locais diferentes.
 */
export function formatRevisionLabel(revision: number | string | null | undefined): string {
  return `dgt-eqt-${revision ?? '?'}`
}

export type ChatValidationReason = 'no_chat_sandbox_validation' | 'validation_stale'

export interface ChatValidationWarning {
  agentId: string
  agentName: string
  reason: ChatValidationReason
  pinnedAgentVersionId: string
  pinnedRevision?: number | null
  validatedAgentVersionId?: string | null
  validatedRevision?: number | null
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
  // Warnings não-bloqueantes (Chat deploy com branch agent Conversational sem
  // validation). Só presente no response de Create/Update; GET não recomputa.
  validationWarnings?: ChatValidationWarning[] | null
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
  if (isSandboxWorkflow(workflow)) return false
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return !!md?.deployedFromAgentId
}

export function deployedAgentId(workflow: Workflow): string | null {
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.deployedFromAgentId ?? null
}

// Pipeline = sequência de N agentes criada pelo editor de implantação avançada.
// Reconhecido pela convenção `deploy-pipeline-{guid}` no id ou pelo marcador
// metadata.deploymentKind === 'pipeline'. Workflows externos criados via API
// (admin) não nascem com nenhum desses marcadores e ficam fora da lista de
// Implantações — mantém o escopo da tela em "o que o PM pode editar aqui".
export function isPipelineDeployment(workflow: Workflow): boolean {
  if (workflow.id.startsWith('deploy-pipeline-')) return true
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.deploymentKind === PIPELINE_DEPLOYMENT_KIND
}

// Routing = Router + N branches (1 agente por intent) + fallback. Workflow
// Graph mode com Switch edge. Reconhecido pela convenção `deploy-routing-{guid}`
// no id ou pelo marcador metadata.deploymentKind === 'routing'.
export function isRoutingDeployment(workflow: Workflow): boolean {
  if (workflow.id.startsWith('deploy-routing-')) return true
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.deploymentKind === ROUTING_DEPLOYMENT_KIND
}

// Chat = Router-pra-chat + N branches Conversational + fallback Conversational.
// Workflow Graph + InputMode=Chat. Restrição cross-project: só projetos com
// chat_deployment_allowed=true conseguem criar. Reconhecido por
// `deploy-chat-{guid}` no id ou metadata.deploymentKind === 'chat'.
export function isChatDeployment(workflow: Workflow): boolean {
  if (isSandboxWorkflow(workflow)) return false
  if (workflow.id.startsWith('deploy-chat-')) return true
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.deploymentKind === CHAT_DEPLOYMENT_KIND
}

export type DeploymentKind = 'single' | 'pipeline' | 'routing' | 'chat'

export function deploymentKindOf(workflow: Workflow): DeploymentKind {
  if (isChatDeployment(workflow)) return 'chat'
  if (isRoutingDeployment(workflow)) return 'routing'
  if (isPipelineDeployment(workflow)) return 'pipeline'
  return 'single'
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

// ── Trigger / execução ──────────────────────────────────────────────────────

export interface TriggerWorkflowBody {
  input?: string | null
  metadata?: Record<string, string>
}

export interface TriggerWorkflowResponse {
  executionId: string
  statusUrl?: string
}

// Quando `workflowVersionId` é informado, propaga via header `x-version` —
// backend pina a execução numa versão específica (canary/A-B/teste de
// versão antiga sem rollback). Ausente = comportamento legado (current).
export const triggerWorkflow = (
  workflowId: string,
  body: TriggerWorkflowBody,
  workflowVersionId?: string | null,
) =>
  post<TriggerWorkflowResponse>(
    `/workflows/${workflowId}/trigger`,
    body,
    workflowVersionId ? { 'x-version': workflowVersionId } : undefined,
  )

// Mesmo shape do trigger mas com `mode=Sandbox` no backend: tools mockadas,
// métricas tagueadas, sem persistência de chat. Usado pelo sandbox de
// implantação (single ou pipeline) — execução é sempre standalone.
export const sandboxWorkflow = (
  workflowId: string,
  body: TriggerWorkflowBody,
  workflowVersionId?: string | null,
) =>
  post<TriggerWorkflowResponse>(
    `/workflows/${workflowId}/sandbox`,
    body,
    workflowVersionId ? { 'x-version': workflowVersionId } : undefined,
  )

export type ExecutionStatus =
  | 'Pending'
  | 'Running'
  | 'Paused'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'

export interface ExecutionSummary {
  executionId: string
  workflowId: string
  // Pin opcional para a WorkflowVersion consumida. null quando a execução
  // rodou contra o estado mutável atual; string com o id da version quando
  // o trigger usou header `x-version` (canary/A/B).
  workflowVersionId?: string | null
  status: ExecutionStatus
  startedAt?: string | null
  completedAt?: string | null
  output?: string | null
  errorMessage?: string | null
  [key: string]: unknown
}

export const getExecution = (executionId: string) =>
  get<ExecutionSummary>(`/executions/${executionId}`)

// ── Eventos SSE da execução ──────────────────────────────────────────────────
// Backend emite via /api/aihub/executions/{id}/stream com format SSE
// `event: <type>\ndata: <json>\n\n`. Tipos relevantes pro sandbox stepwise
// estão modelados como discriminated union; outros tipos viajam como `unknown`
// no campo `payload` e podem ser inspecionados pelo caller.
export type WorkflowEvent =
  | { type: 'workflow_started'; payload: WorkflowStartedPayload }
  | { type: 'node_started'; payload: NodeStartedPayload }
  | { type: 'node_completed'; payload: NodeCompletedPayload }
  | { type: 'workflow_completed'; payload: WorkflowCompletedPayload }
  | { type: 'workflow_failed' | 'workflow_cancelled' | 'error'; payload: WorkflowFailedPayload }
  | { type: 'hitl_required'; payload: unknown }
  | { type: 'tool_invocation'; payload: unknown }
  | { type: 'state_delta'; payload: unknown }
  | { type: 'token'; payload: unknown }
  | { type: string; payload: unknown }

export interface WorkflowStartedPayload {
  executionId?: string
  workflowId?: string
  [key: string]: unknown
}

export interface NodeStartedPayload {
  nodeId?: string
  nodeType?: string
  agentId?: string
  agentName?: string
  timestamp?: string
  [key: string]: unknown
}

export interface NodeCompletedPayload {
  nodeId?: string
  nodeType?: string
  agentId?: string
  agentName?: string
  output?: string
  timestamp?: string
  [key: string]: unknown
}

export interface WorkflowCompletedPayload {
  output?: string
  [key: string]: unknown
}

export interface WorkflowFailedPayload {
  error?: string
  message?: string
  [key: string]: unknown
}

// Tipos de evento que o sandbox stepwise consome do SSE pra atualizar a
// timeline. Outros tipos (token, hitl_required, tool_invocation, state_delta)
// são publicados pelo backend mas o sandbox sequencial não os usa hoje.
export const STREAM_EVENT_TYPES = [
  'workflow_started',
  'node_started',
  'node_completed',
  'workflow_completed',
  'workflow_failed',
  'workflow_cancelled',
  'error',
] as const

export type StreamEventType = (typeof STREAM_EVENT_TYPES)[number]

// Constrói URL absoluta do SSE de execução com fallback de identity em query
// param — EventSource API não envia headers custom no browser.
export function executionStreamUrl(executionId: string, account: string): string {
  const qs = new URLSearchParams({ account }).toString()
  return `${API_BASE_URL}/executions/${executionId}/stream?${qs}`
}
