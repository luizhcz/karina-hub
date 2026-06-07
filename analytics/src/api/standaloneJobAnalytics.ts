import { get } from './client'

// Espelha DTOs em
// src/EfsAiHub.Core.Abstractions/Observability/StandaloneJobAnalytics.cs.
// Endpoints em
// src/EfsAiHub.Host.Api/Controllers/StandaloneJobAnalyticsController.cs.

export interface StandaloneJobByWorkflow {
  workflowId: string
  total: number
  completed: number
  failed: number
  /** Concluídos / (Concluídos + Falhos). 0..1. */
  successRate: number
  totalAttempts: number
  avgTotalMs: number
  p95TotalMs: number
}

export interface StandaloneJobOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  totalJobs: number
  completed: number
  failed: number
  cancelled: number
  queued: number
  running: number
  successRate: number
  totalAttempts: number
  retryRate: number
  /** Latência queue → running em ms. */
  p95QueueMs: number
  /** Latência ponta a ponta CreatedAt → CompletedAt em ms. */
  p95TotalMs: number
  workflows: StandaloneJobByWorkflow[]
}

export interface StandaloneJobTimeseriesBucket {
  bucket: string
  created: number
  completed: number
  failed: number
  p95QueueMs: number
}

export type StandaloneJobGranularity = 'day' | 'hour'

function buildQs(projectId: string, from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  qs.set('projectId', projectId)
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  return `?${qs.toString()}`
}

export function getStandaloneJobSummary(
  projectId: string,
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<StandaloneJobOverview> {
  return get<StandaloneJobOverview>(
    `/analytics/standalone-jobs/summary${buildQs(projectId, from, to)}`,
    init,
  )
}

export function getStandaloneJobTimeseries(
  projectId: string,
  groupBy: StandaloneJobGranularity = 'day',
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<StandaloneJobTimeseriesBucket[]> {
  return get<StandaloneJobTimeseriesBucket[]>(
    `/analytics/standalone-jobs/timeseries${buildQs(projectId, from, to, { groupBy })}`,
    init,
  )
}
