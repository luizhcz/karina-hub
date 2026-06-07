import { get } from './client'

// Espelha os DTOs do backend em
// src/EfsAiHub.Core.Abstractions/Observability/ILlmTokenUsageRepository.cs.
// Endpoints em src/EfsAiHub.Host.Api/Controllers/TokenUsageController.cs.
//
// Nota: ao contrário do ProjectAnalytics, essas summaries NÃO trazem campo
// `costUsd` — só tokens + modelId + duration. Cost cross-projeto/workflow é
// inferida indiretamente via ProjectOverview/AgentBreakdown.

export interface ThroughputBucket {
  /** ISO-8601 da borda inicial do bucket (UTC). Sempre por hora. */
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

function buildRangeQs(from?: string, to?: string): string {
  const qs = new URLSearchParams()
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  const s = qs.toString()
  return s ? `?${s}` : ''
}

export function getThroughput(
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<ThroughputResult> {
  return get<ThroughputResult>(`/token-usage/throughput${buildRangeQs(from, to)}`, init)
}

export function getWorkflowsSummary(
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<WorkflowTokenSummary[]> {
  return get<WorkflowTokenSummary[]>(
    `/token-usage/workflows/summary${buildRangeQs(from, to)}`,
    init,
  )
}

export function getProjectsSummary(
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<ProjectTokenSummary[]> {
  return get<ProjectTokenSummary[]>(
    `/token-usage/projects/summary${buildRangeQs(from, to)}`,
    init,
  )
}
