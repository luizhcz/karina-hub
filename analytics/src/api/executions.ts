import { get } from './client'

// Espelha DTOs do backend em
// src/EfsAiHub.Host.Api/Endpoints/Responses/ExecutionResponse.cs.
// Endpoints em src/EfsAiHub.Host.Api/Controllers/ExecutionsController.cs.
//
// Observação importante: o controller atual NÃO faz project-scoping — devolve
// execuções de todos os projetos do tenant. A tela faz filter client-side via
// `metadata.projectId` enquanto o backend não ganha o mesmo padrão de scope
// dos demais endpoints analytics. Marcado como follow-up.

export type ExecutionStatus =
  | 'Pending'
  | 'Running'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Paused'

export interface ExecutionStep {
  stepId: string
  agentId: string
  agentName: string
  status: ExecutionStatus
  output?: string | null
  startedAt: string
  completedAt?: string | null
  tokensUsed: number
}

export interface ExecutionItem {
  executionId: string
  workflowId: string
  workflowVersionId?: string | null
  status: ExecutionStatus
  input?: string | null
  output?: string | null
  errorMessage?: string | null
  startedAt: string
  completedAt?: string | null
  /**
   * Metadata livre — controller serializa `Dictionary<string,string>`.
   * Chaves observadas em prod: projectId, conversationId, userId, userType.
   */
  metadata: Record<string, string>
}

export interface ExecutionDetail extends ExecutionItem {
  steps: ExecutionStep[]
}

/** Node retornado pelo endpoint /full — granularidade por agente/executor invocado. */
export interface ExecutionNode {
  nodeId: string
  executionId: string
  /** "agent" | "executor" — agente declarado vs executor de código. */
  nodeType: string
  status: string
  startedAt: string
  completedAt?: string | null
  output?: string | null
  /** Quando true, o output foi truncado pelo backend (limite grande). */
  outputTruncated: boolean
  iteration?: number | null
  tokensUsed: number
  /** ID canônico da assistant message — bate com chat_messages.MessageId. */
  messageId?: string | null
}

/** Invocação de tool dentro de uma execução. */
export interface ExecutionToolCall {
  id?: number | string
  executionId: string
  agentId: string
  toolName: string
  arguments?: unknown
  result?: string | null
  durationMs: number
  success: boolean
  errorMessage?: string | null
  createdAt: string
}

/** Evento do audit log da execução — útil pra timeline. */
export interface ExecutionAuditEvent {
  sequenceId?: number
  eventType: string
  payload?: unknown
  timestamp: string
}

/**
 * Dump completo de uma execução (rota /executions/{id}/full).
 * Mais rico que /executions/{id} porque inclui nodes + tools + events. O
 * /executions/{id} hoje devolve `steps: []` sempre — preferimos /full pra
 * UI que precisa de drill-down (drawer da Execuções).
 */
export interface ExecutionFull {
  execution: ExecutionItem
  nodes: ExecutionNode[]
  tools: ExecutionToolCall[]
  events: ExecutionAuditEvent[]
}

export interface ExecutionListResponse {
  items: ExecutionItem[]
  total: number
  page: number
  pageSize: number
}

function buildQs(extras: Record<string, string | undefined>): string {
  const qs = new URLSearchParams()
  for (const [k, v] of Object.entries(extras)) {
    if (v !== undefined && v !== '') qs.set(k, v)
  }
  const s = qs.toString()
  return s ? `?${s}` : ''
}

export function listExecutions(
  filters: {
    workflowId?: string
    status?: ExecutionStatus | string
    from?: string
    to?: string
    page?: number
    pageSize?: number
  },
  init?: RequestInit,
): Promise<ExecutionListResponse> {
  const qs = buildQs({
    workflowId: filters.workflowId,
    status: filters.status,
    from: filters.from,
    to: filters.to,
    page: filters.page ? String(filters.page) : undefined,
    pageSize: filters.pageSize ? String(filters.pageSize) : undefined,
  })
  return get<ExecutionListResponse>(`/executions${qs}`, init)
}

export function getExecution(
  executionId: string,
  init?: RequestInit,
): Promise<ExecutionDetail> {
  return get<ExecutionDetail>(`/executions/${encodeURIComponent(executionId)}`, init)
}

/**
 * Dump completo (execution + nodes + tools + events). Preferido pelo drawer
 * de Execuções porque o /executions/{id} retorna steps=[] sempre (a model
 * não materializa essa coleção). Nodes do /full dão o equivalente + mais.
 */
export function getExecutionFull(
  executionId: string,
  init?: RequestInit,
): Promise<ExecutionFull> {
  return get<ExecutionFull>(`/executions/${encodeURIComponent(executionId)}/full`, init)
}

/** Calcula duração em ms (StartedAt → CompletedAt). null quando ainda rodando. */
export function executionDurationMs(item: ExecutionItem): number | null {
  if (!item.completedAt) return null
  const start = new Date(item.startedAt).getTime()
  const end = new Date(item.completedAt).getTime()
  if (!Number.isFinite(start) || !Number.isFinite(end)) return null
  return Math.max(0, end - start)
}
