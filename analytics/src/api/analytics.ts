import { get } from './client'

// Espelha os DTOs do backend em
// src/EfsAiHub.Core.Abstractions/Observability/ProjectAnalytics.cs (mvp/ usa o
// mesmo arquivo). Nomes camelCase porque ASP.NET serializa por padrão assim.
// Endpoints definidos em
// src/EfsAiHub.Host.Api/Controllers/ProjectAnalyticsController.cs.

export interface AgentMiniRow {
  agentId: string
  /** Hidratado via JOIN com agent_definitions. Null se o agente foi deletado. */
  agentName?: string | null
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
  /** ISO-8601 da borda inicial do bucket (UTC). */
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
  /** 0..1 (>1 quando estourou). null quando o limite não está configurado. */
  tokensUsagePct?: number | null
  costUsagePct?: number | null
  exceeded: boolean
}

export type Granularity = 'day' | 'hour'

function buildRangeQs(from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) {
    for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  }
  const s = qs.toString()
  return s ? `?${s}` : ''
}

/**
 * @param ownedOnly Quando true, totais e topAgents incluem só agentes cujo
 *  ProjectId é o do request (descarta Visibility=global). Default false casa
 *  com a UI default — "ver tudo que rodou no meu projeto".
 */
export function getProjectOverview(
  projectId: string,
  from?: string,
  to?: string,
  ownedOnly = false,
  init?: RequestInit,
): Promise<ProjectOverview> {
  const qs = buildRangeQs(from, to, ownedOnly ? { ownedOnly: 'true' } : undefined)
  return get<ProjectOverview>(`/analytics/projects/${projectId}/overview${qs}`, init)
}

export function getProjectTimeseries(
  projectId: string,
  groupBy: Granularity = 'day',
  from?: string,
  to?: string,
  excludeAgentIds?: readonly string[],
  ownedOnly = false,
  init?: RequestInit,
): Promise<ProjectTimeseriesBucket[]> {
  const extras: Record<string, string> = { groupBy }
  if (excludeAgentIds && excludeAgentIds.length > 0) {
    extras.excludeAgentIds = excludeAgentIds.join(',')
  }
  if (ownedOnly) extras.ownedOnly = 'true'
  const qs = buildRangeQs(from, to, extras)
  return get<ProjectTimeseriesBucket[]>(
    `/analytics/projects/${projectId}/timeseries${qs}`,
    init,
  )
}

export function getProjectAgents(
  projectId: string,
  top = 20,
  from?: string,
  to?: string,
  ownedOnly = false,
  init?: RequestInit,
): Promise<ProjectAgentBreakdown[]> {
  const extras: Record<string, string> = { top: String(top) }
  if (ownedOnly) extras.ownedOnly = 'true'
  const qs = buildRangeQs(from, to, extras)
  return get<ProjectAgentBreakdown[]>(`/analytics/projects/${projectId}/agents${qs}`, init)
}

export function getProjectBudget(
  projectId: string,
  init?: RequestInit,
): Promise<ProjectBudgetStatus> {
  return get<ProjectBudgetStatus>(`/analytics/projects/${projectId}/budget`, init)
}
