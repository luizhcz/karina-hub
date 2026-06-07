import { get } from './client'

// Espelha DTOs em
// src/EfsAiHub.Core.Abstractions/Observability/FeedbackAnalytics.cs.
// Endpoints em
// src/EfsAiHub.Host.Api/Controllers/FeedbackAnalyticsController.cs.

export type Sentiment = 1 | -1

export interface FeedbackTopMessage {
  messageId: string
  feedbackCount: number
  positives: number
  negatives: number
  /** Primeiros ~200 chars de chat_messages.Content. Null se mensagem deletada. */
  messagePreview?: string | null
  conversationId?: string | null
}

export interface FeedbackOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  total: number
  positives: number
  negatives: number
  /** Positives / (Positives + Negatives). 0..1. */
  satisfactionRate: number
  distinctMessages: number
  distinctUsers: number
  withCommentCount: number
  topMessages: FeedbackTopMessage[]
}

export interface FeedbackTimeseriesBucket {
  bucket: string
  count: number
  positives: number
  negatives: number
}

export interface FeedbackRecentRow {
  feedbackId: string
  messageId: string
  conversationId?: string | null
  executionId?: string | null
  /** -1 (dislike) ou +1 (like). */
  sentiment: number
  comment?: string | null
  userId?: string | null
  messagePreview?: string | null
  createdAt: string
}

export interface FeedbackRecentResponse {
  items: FeedbackRecentRow[]
  total: number
  page: number
  pageSize: number
}

export type FeedbackGranularity = 'day' | 'hour'

function buildQs(projectId: string, from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  qs.set('projectId', projectId)
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  return `?${qs.toString()}`
}

export function getFeedbackSummary(
  projectId: string,
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<FeedbackOverview> {
  return get<FeedbackOverview>(`/analytics/feedback/summary${buildQs(projectId, from, to)}`, init)
}

export function getFeedbackTimeseries(
  projectId: string,
  groupBy: FeedbackGranularity = 'day',
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<FeedbackTimeseriesBucket[]> {
  return get<FeedbackTimeseriesBucket[]>(
    `/analytics/feedback/timeseries${buildQs(projectId, from, to, { groupBy })}`,
    init,
  )
}

export interface FeedbackRecentFilters {
  /** null | undefined = todos; 1 = likes; -1 = dislikes. */
  sentiment?: Sentiment | null
  page?: number
  pageSize?: number
}

export function getFeedbackRecent(
  projectId: string,
  from?: string,
  to?: string,
  filters: FeedbackRecentFilters = {},
  init?: RequestInit,
): Promise<FeedbackRecentResponse> {
  const extras: Record<string, string> = {}
  if (filters.sentiment === 1 || filters.sentiment === -1) {
    extras.sentiment = String(filters.sentiment)
  }
  if (filters.page) extras.page = String(filters.page)
  if (filters.pageSize) extras.pageSize = String(filters.pageSize)
  return get<FeedbackRecentResponse>(
    `/analytics/feedback/recent${buildQs(projectId, from, to, extras)}`,
    init,
  )
}
