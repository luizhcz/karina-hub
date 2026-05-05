import { get } from './client'

// Espelha os DTOs do backend em src/EfsAiHub.Core.Abstractions/Observability/ProjectAnalytics.cs.
// Mantemos os nomes em camelCase porque o ASP.NET serializa pra camelCase por padrão.

export interface AgentMiniRow {
  agentId: string
  totalTokens: number
  costUsd: number
  calls: number
}

export interface ProjectOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  totalCostUsd: number
  totalTokens: number
  totalCalls: number
  totalExecutions: number
  completed: number
  failed: number
  /** Sucesso/(Sucesso+Falha). 0 quando ainda não há execução resolvida. */
  successRate: number
  topAgents: AgentMiniRow[]
}

export interface ProjectTimeseriesBucket {
  bucket: string
  costUsd: number
  tokens: number
  calls: number
  executions: number
  completed: number
  failed: number
}

export interface ProjectAgentBreakdown {
  agentId: string
  agentName?: string | null
  modelId?: string | null
  calls: number
  totalTokens: number
  costUsd: number
  avgDurationMs: number
  p95DurationMs: number
  /** Falhas/(Sucessos+Falhas). 0 quando não há execução resolvida. */
  errorRate: number
}

export interface ProjectBudgetStatus {
  projectId: string
  maxTokensPerDay?: number | null
  maxCostUsdPerDay?: number | null
  todayTokens: number
  todayCostUsd: number
  /** 0..1 (ou >1 quando estourou). null quando o limite não está configurado. */
  tokensUsagePct?: number | null
  costUsagePct?: number | null
  exceeded: boolean
}

export type Granularity = 'day' | 'hour'

export const getProjectOverview = (projectId: string, from?: string, to?: string) => {
  const qs = new URLSearchParams()
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  const suffix = qs.toString() ? `?${qs}` : ''
  return get<ProjectOverview>(`/analytics/projects/${projectId}/overview${suffix}`)
}

export const getProjectTimeseries = (
  projectId: string,
  groupBy: Granularity = 'day',
  from?: string,
  to?: string,
  excludeAgentIds?: readonly string[],
) => {
  const qs = new URLSearchParams({ groupBy })
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (excludeAgentIds && excludeAgentIds.length > 0) {
    qs.set('excludeAgentIds', excludeAgentIds.join(','))
  }
  return get<ProjectTimeseriesBucket[]>(
    `/analytics/projects/${projectId}/timeseries?${qs}`,
  )
}

export const getProjectAgents = (
  projectId: string,
  top = 20,
  from?: string,
  to?: string,
) => {
  const qs = new URLSearchParams({ top: String(top) })
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  return get<ProjectAgentBreakdown[]>(`/analytics/projects/${projectId}/agents?${qs}`)
}

export const getProjectBudget = (projectId: string) =>
  get<ProjectBudgetStatus>(`/analytics/projects/${projectId}/budget`)
