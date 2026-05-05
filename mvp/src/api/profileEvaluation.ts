import { post, get } from './client'
import { getIdentity } from '../stores/identity'

export type AutoDeployPreset = 'basic' | 'medium' | 'advanced'

export interface AutoDeployResponse {
  runId: string | null
  testSetVersionId: string | null
  evaluatorConfigVersionId: string | null
  preset: AutoDeployPreset
  caseCount: number
  estimatedCostUsd: number
  estimatedDurationSeconds: number
  status: string | null
  deduplicatedFromExisting: boolean
  generatorFailed: boolean
}

export interface EvalRunSummary {
  runId: string
  status: string
  agentVersionId: string
  testSetVersionId: string
  evaluatorConfigVersionId: string
  triggerSource: string
  triggerContext?: { preset?: string; source?: string } | null
  casesTotal: number
  casesCompleted: number
  casesPassed: number
  casesFailed: number
  avgScore?: number | null
  startedAt?: string | null
  completedAt?: string | null
  createdAt: string
  lastError?: string | null
}

export interface EvalProgressEvent {
  status: string
  casesTotal: number
  casesCompleted: number
  casesPassed: number
  casesFailed: number
  avgScore: number | null
  totalCostUsd: number
  totalTokens: number
  lastError: string | null
  startedAt: string | null
  completedAt: string | null
}

export interface EvalResultDetail {
  resultId: string
  caseId: string
  evaluatorName: string
  bindingIndex: number
  repetitionIndex: number
  score: number | null
  passed: boolean
  reason: string | null
  outputContent: string | null
  judgeModel: string | null
  latencyMs: number | null
  costUsd: number | null
  inputTokens: number | null
  outputTokens: number | null
  createdAt: string
}

const PRESET_META: Record<AutoDeployPreset, { label: string; cost: string; duration: string }> = {
  basic:    { label: 'Básica',   cost: '$0',     duration: '<30s' },
  medium:   { label: 'Média',    cost: '~$0.10', duration: '~1min' },
  advanced: { label: 'Avançada', cost: '~$0.50', duration: '~3min' },
}

export function presetMeta(preset: AutoDeployPreset) {
  return PRESET_META[preset]
}

export async function runAutoDeploy(
  agentId: string,
  preset: AutoDeployPreset,
  deployedFromWorkflowId?: string,
): Promise<AutoDeployResponse> {
  return post<AutoDeployResponse>(`/agents/${encodeURIComponent(agentId)}/evaluations/auto-deploy`, {
    preset,
    deployedFromWorkflowId,
  })
}

export async function getEvalRun(runId: string): Promise<EvalRunSummary> {
  return get<EvalRunSummary>(`/evaluations/runs/${encodeURIComponent(runId)}`)
}

export async function listEvalRunsByAgent(
  agentId: string,
  take = 1,
): Promise<EvalRunSummary[]> {
  return get<EvalRunSummary[]>(`/agents/${encodeURIComponent(agentId)}/evaluations/runs?take=${take}`)
}

export async function listResultsByRun(
  runId: string,
  filters?: { passed?: boolean; evaluator?: string; take?: number },
): Promise<EvalResultDetail[]> {
  const params = new URLSearchParams()
  if (filters?.passed !== undefined) params.set('passed', String(filters.passed))
  if (filters?.evaluator) params.set('evaluator', filters.evaluator)
  if (filters?.take !== undefined) params.set('take', String(filters.take))
  const qs = params.toString()
  return get<EvalResultDetail[]>(
    `/evaluations/runs/${encodeURIComponent(runId)}/results${qs ? `?${qs}` : ''}`,
  )
}

/**
 * Stream SSE de progresso da run via EventSource. Identidade vai por query
 * param (EventSource não envia headers customizados). Fecha em status terminal
 * ou em abort do signal.
 */
export function streamEvalRun(
  runId: string,
  onProgress: (e: EvalProgressEvent) => void,
  onDone: (e: EvalProgressEvent) => void,
  onError?: (err: Event | Error) => void,
  signal?: AbortSignal,
): () => void {
  const id = getIdentity()
  const url = id?.projectId
    ? `/api/evaluations/runs/${encodeURIComponent(runId)}/stream?projectId=${encodeURIComponent(id.projectId)}`
    : `/api/evaluations/runs/${encodeURIComponent(runId)}/stream`

  const es = new EventSource(url)
  const close = () => es.close()
  signal?.addEventListener('abort', close, { once: true })

  es.addEventListener('progress', (ev: MessageEvent) => {
    try {
      const data = JSON.parse(ev.data) as EvalProgressEvent
      onProgress(data)
    } catch {
      // payload não-JSON é ignorado (keep-alive comment)
    }
  })

  es.addEventListener('done', (ev: MessageEvent) => {
    try {
      const data = JSON.parse(ev.data) as EvalProgressEvent
      onDone(data)
    } catch {
      // ignore
    } finally {
      es.close()
    }
  })

  es.addEventListener('error', (ev: Event) => {
    onError?.(ev)
    // EventSource auto-reconnect; mas em readyState=CLOSED desistimos
    if (es.readyState === EventSource.CLOSED) close()
  })

  return close
}
