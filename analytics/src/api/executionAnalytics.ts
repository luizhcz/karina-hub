import { get } from './client'

// Espelha DTOs do backend em
// src/EfsAiHub.Core.Orchestration/Workflows/IExecutionAnalyticsRepository.cs.
// Endpoints em src/EfsAiHub.Host.Api/Controllers/AnalyticsController.cs
// (rota /api/aihub/analytics/executions/*).
//
// Sempre project-scoped — sandbox excluído via v_production_executions
// (filtro feito no backend). `?workflowId=` permite drill-down opcional pra
// um workflow específico (não usado no V1 da tela Confiabilidade).

export interface ExecutionSummary {
  total: number
  completed: number
  failed: number
  cancelled: number
  running: number
  pending: number
  /**
   * Percent points (0..100, 1 decimal) — DIFERENTE de ProjectAnalytics que
   * devolve fração 0..1. Use formatPercentPoints, não formatPercent.
   */
  successRate: number
  avgDurationMs: number
  p50Ms: number
  p95Ms: number
}

export interface ExecutionTimeseriesBucket {
  bucket: string
  total: number
  completed: number
  failed: number
  avgDurationMs: number
}

export interface ExecutionFailureRow {
  /** Ex.: Timeout, BudgetExceeded, HitlRejected, ToolError, FrameworkError, Unknown. */
  category: string
  count: number
}

export type ExecutionGranularity = 'day' | 'hour'

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

export function getExecutionSummary(
  projectId: string,
  from?: string,
  to?: string,
  workflowId?: string,
  init?: RequestInit,
): Promise<ExecutionSummary> {
  const extras = workflowId ? { workflowId } : undefined
  return get<ExecutionSummary>(`/analytics/executions/summary${buildQs(projectId, from, to, extras)}`, init)
}

export function getExecutionTimeseries(
  projectId: string,
  groupBy: ExecutionGranularity = 'day',
  from?: string,
  to?: string,
  workflowId?: string,
  init?: RequestInit,
): Promise<ExecutionTimeseriesBucket[]> {
  const extras: Record<string, string> = { groupBy }
  if (workflowId) extras.workflowId = workflowId
  // Controller envelopa em { buckets } — desembrulha aqui pra que o caller
  // receba sempre array, mesmo padrão de ferramentas.
  return get<{ buckets: ExecutionTimeseriesBucket[] }>(
    `/analytics/executions/timeseries${buildQs(projectId, from, to, extras)}`,
    init,
  ).then((r) => r.buckets)
}

export function getExecutionFailureBreakdown(
  projectId: string,
  from?: string,
  to?: string,
  workflowId?: string,
  init?: RequestInit,
): Promise<ExecutionFailureRow[]> {
  const extras = workflowId ? { workflowId } : undefined
  return get<{ breakdown: ExecutionFailureRow[] }>(
    `/analytics/executions/failure-breakdown${buildQs(projectId, from, to, extras)}`,
    init,
  ).then((r) => r.breakdown)
}
