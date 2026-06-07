import { get } from './client'

// Espelha DTOs em
// src/EfsAiHub.Core.Abstractions/Observability/RouterDecisionAnalytics.cs.
// Endpoints em
// src/EfsAiHub.Host.Api/Controllers/RouterDecisionAnalyticsController.cs.
//
// Particularidade: ~3% dos outputs do Router têm prefixo "Assistant: " ou
// JSON malformado; a extração no backend descarta esses casos
// silenciosamente. Total reportado já é o "parseável" — sub-amostragem
// aceitável pra analytics agregado.

export interface RouterIntentStats {
  intent: string
  count: number
  avgConfidence: number
  p50Confidence: number
  p95Confidence: number
  minConfidence: number
}

export interface RouterDecisionOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  totalDecisions: number
  distinctIntents: number
  avgConfidence: number
  p50Confidence: number
  lowConfidenceCount: number
  lowConfidenceRate: number
  ambiguityCount: number
  ambiguityRate: number
  intents: RouterIntentStats[]
}

export interface RouterDecisionTimeseriesBucket {
  bucket: string
  total: number
  avgConfidence: number
  lowConfidenceCount: number
  ambiguityCount: number
}

export type RouterGranularity = 'day' | 'hour'

function buildQs(projectId: string, from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  qs.set('projectId', projectId)
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  return `?${qs.toString()}`
}

export function getRouterDecisionSummary(
  projectId: string,
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<RouterDecisionOverview> {
  return get<RouterDecisionOverview>(`/analytics/router-decisions/summary${buildQs(projectId, from, to)}`, init)
}

export function getRouterDecisionTimeseries(
  projectId: string,
  groupBy: RouterGranularity = 'day',
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<RouterDecisionTimeseriesBucket[]> {
  return get<RouterDecisionTimeseriesBucket[]>(
    `/analytics/router-decisions/timeseries${buildQs(projectId, from, to, { groupBy })}`,
    init,
  )
}
