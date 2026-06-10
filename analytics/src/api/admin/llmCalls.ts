import { get, put } from '../client'

// ─── Capture config ─────────────────────────────────────────────────────────

export interface LlmCaptureConfig {
  enabled: boolean
  projectIds: string[] | null
  agentIds: string[] | null
  workflowIds: string[] | null
  expiresAt: string | null
  enabledBy: string | null
  enabledAt: string | null
  updatedAt: string
  secondsRemaining: number | null
}

export interface UpdateCaptureConfigRequest {
  enabled: boolean
  projectIds?: string[]
  agentIds?: string[]
  workflowIds?: string[]
  /** 1..168 horas (até 7 dias). Omita pra captura sem auto-off. */
  durationHours?: number
}

export function getCaptureConfig(): Promise<LlmCaptureConfig> {
  return get<LlmCaptureConfig>('/admin/llm-capture/config')
}

export function updateCaptureConfig(body: UpdateCaptureConfigRequest) {
  return put<{ enabled: boolean; expiresAt: string | null; enabledBy: string | null }>(
    '/admin/llm-capture/config',
    body,
  )
}

// ─── Calls list/detail ──────────────────────────────────────────────────────

export interface LlmCallSummary {
  id: number
  turnId: string
  attemptIndex: number
  agentId: string
  projectId: string | null
  executionId: string | null
  intent: string | null
  provider: string
  providerResolved: string
  model: string
  status: string
  durationMs: number
  inputTokens: number
  outputTokens: number
  truncated: boolean
  createdAt: string
}

export interface LlmCallListResponse {
  page: number
  size: number
  total: number
  items: LlmCallSummary[]
}

export interface PromptSection {
  source: string
  contributorType: string
  messageIndex: number | null
  charOffset: number | null
  charLength: number | null
  note: string | null
}

export interface LlmCallDetail {
  id: number
  turnId: string
  attemptIndex: number
  executionId: string | null
  workflowId: string | null
  agentId: string
  agentVersionId: string | null
  projectId: string | null
  provider: string
  providerResolved: string
  model: string
  intent: string | null
  status: string
  errorMessage: string | null
  durationMs: number
  inputTokens: number
  outputTokens: number
  cachedTokens: number
  requestSizeBytes: number
  responseSizeBytes: number
  truncated: boolean
  request: unknown
  response: unknown
  composition: PromptSection[] | null
  chatOptions: unknown
  createdAt: string
}

export interface ListCallsParams {
  agentId?: string
  projectId?: string
  intent?: string
  status?: string
  executionId?: string
  from?: string
  to?: string
  minDurationMs?: number
  page?: number
  size?: number
}

export function listLlmCalls(params: ListCallsParams = {}): Promise<LlmCallListResponse> {
  const qs = new URLSearchParams()
  if (params.agentId) qs.set('agentId', params.agentId)
  if (params.projectId) qs.set('projectId', params.projectId)
  if (params.intent) qs.set('intent', params.intent)
  if (params.status) qs.set('status', params.status)
  if (params.executionId) qs.set('executionId', params.executionId)
  if (params.from) qs.set('from', params.from)
  if (params.to) qs.set('to', params.to)
  if (params.minDurationMs !== undefined) qs.set('minDurationMs', String(params.minDurationMs))
  if (params.page) qs.set('page', String(params.page))
  if (params.size) qs.set('size', String(params.size))
  const suffix = qs.toString() ? `?${qs}` : ''
  return get<LlmCallListResponse>(`/admin/llm-calls${suffix}`)
}

export function getLlmCall(id: number): Promise<LlmCallDetail> {
  return get<LlmCallDetail>(`/admin/llm-calls/${id}`)
}

export function exportLlmCallCurl(id: number): Promise<{ command: string }> {
  return get<{ command: string }>(`/admin/llm-calls/${id}/curl`)
}

export function diffLlmCall(id: number, vs?: number) {
  const suffix = vs !== undefined ? `?vs=${vs}` : ''
  return get<{
    current: LlmCallDetail
    previous: LlmCallDetail | null
    diff: { field: string; before: unknown; after: unknown }[]
  }>(`/admin/llm-calls/${id}/diff${suffix}`)
}

export function recentForDiff(id: number, limit = 10): Promise<LlmCallSummary[]> {
  return get<LlmCallSummary[]>(`/admin/llm-calls/${id}/recent-for-diff?limit=${limit}`)
}
