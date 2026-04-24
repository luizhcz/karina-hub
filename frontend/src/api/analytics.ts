import { get } from './client'
import { useQuery } from '@tanstack/react-query'


export interface ExecutionSummary {
  total: number
  completed: number
  failed: number
  cancelled: number
  running: number
  pending: number
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

export interface ExecutionTimeseries {
  buckets: ExecutionTimeseriesBucket[]
}

/**
 * Breakdown de falhas por ErrorCategory do backend.
 * Espelha a tag `error.category` da métrica OTel `workflows.failed`.
 */
export interface ExecutionFailureBreakdown {
  category: string
  count: number
}

export interface ExecutionFailureBreakdownResponse {
  breakdown: ExecutionFailureBreakdown[]
}

export interface AnalyticsParams {
  from?: string
  to?: string
  workflowId?: string
}

export interface TimeseriesParams extends AnalyticsParams {
  groupBy?: string
}


export const KEYS = {
  summary: (params?: AnalyticsParams) => ['analytics', 'summary', params] as const,
  timeseries: (params?: TimeseriesParams) => ['analytics', 'timeseries', params] as const,
  failureBreakdown: (params?: AnalyticsParams) => ['analytics', 'failure-breakdown', params] as const,
}


export const getExecutionSummary = (params?: AnalyticsParams) =>
  get<ExecutionSummary>('/analytics/executions/summary', params)

export const getExecutionTimeseries = (params?: TimeseriesParams) =>
  get<ExecutionTimeseries>('/analytics/executions/timeseries', params)

export const getExecutionFailureBreakdown = (params?: AnalyticsParams) =>
  get<ExecutionFailureBreakdownResponse>('/analytics/executions/failure-breakdown', params)


export function useExecutionSummary(params?: AnalyticsParams) {
  return useQuery({
    queryKey: KEYS.summary(params),
    queryFn: () => getExecutionSummary(params),
  })
}

export function useExecutionTimeseries(params?: TimeseriesParams) {
  return useQuery({
    queryKey: KEYS.timeseries(params),
    queryFn: () => getExecutionTimeseries(params),
  })
}

export function useExecutionFailureBreakdown(params?: AnalyticsParams) {
  return useQuery({
    queryKey: KEYS.failureBreakdown(params),
    queryFn: () => getExecutionFailureBreakdown(params),
  })
}
