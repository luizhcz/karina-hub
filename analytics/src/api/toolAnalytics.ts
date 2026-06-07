import { get } from './client'

// Espelha DTOs do backend em
// src/EfsAiHub.Core.Abstractions/Observability/ToolAnalytics.cs.
// Endpoints em src/EfsAiHub.Host.Api/Controllers/ToolAnalyticsController.cs.
//
// Production-only: o backend faz INNER JOIN com v_production_executions —
// chamadas de sandbox session NÃO aparecem aqui. Pra ver sandbox, tela
// separada (a definir).

export interface ToolUsageRow {
  toolName: string
  calls: number
  succeeded: number
  failed: number
  /** Falhas / (sucessos+falhas). 0 quando ainda não houve chamada. */
  errorRate: number
  avgDurationMs: number
  p50DurationMs: number
  p95DurationMs: number
  maxDurationMs: number
  distinctAgents: number
}

export interface ToolUsageOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  totalCalls: number
  totalSucceeded: number
  totalFailed: number
  distinctTools: number
  errorRate: number
  p95DurationMs: number
  tools: ToolUsageRow[]
}

export interface ToolTimeseriesBucket {
  /** ISO-8601 da borda inicial do bucket (UTC). */
  bucket: string
  calls: number
  succeeded: number
  failed: number
  avgDurationMs: number
  p95DurationMs: number
}

export type ToolGranularity = 'day' | 'hour'

function buildQs(projectId: string, from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  qs.set('projectId', projectId)
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) {
    for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  }
  return `?${qs.toString()}`
}

export function getToolSummary(
  projectId: string,
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<ToolUsageOverview> {
  return get<ToolUsageOverview>(`/analytics/tools/summary${buildQs(projectId, from, to)}`, init)
}

export function getToolTimeseries(
  projectId: string,
  groupBy: ToolGranularity = 'day',
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<ToolTimeseriesBucket[]> {
  return get<ToolTimeseriesBucket[]>(
    `/analytics/tools/timeseries${buildQs(projectId, from, to, { groupBy })}`,
    init,
  )
}
