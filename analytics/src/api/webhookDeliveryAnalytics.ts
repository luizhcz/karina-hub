import { get } from './client'

// Espelha DTOs em
// src/EfsAiHub.Core.Abstractions/Observability/WebhookDeliveryAnalytics.cs.
// Endpoints em
// src/EfsAiHub.Host.Api/Controllers/WebhookDeliveryAnalyticsController.cs.

export interface WebhookStatusCodeRow {
  /** "2xx" | "3xx" | "4xx" | "5xx" | "no-response" | "other" */
  bucket: string
  count: number
}

export interface WebhookHostRow {
  host: string
  total: number
  delivered: number
  failed: number
  deliveryRate: number
}

export interface WebhookDeliveryOverview {
  projectId: string
  periodFrom: string
  periodTo: string
  totalDeliveries: number
  delivered: number
  failed: number
  pending: number
  delivering: number
  deliveryRate: number
  p95DeliveryMs: number
  statusCodes: WebhookStatusCodeRow[]
  hosts: WebhookHostRow[]
}

export interface WebhookDeliveryTimeseriesBucket {
  bucket: string
  created: number
  delivered: number
  failed: number
  p95DeliveryMs: number
}

export type WebhookGranularity = 'day' | 'hour'

function buildQs(projectId: string, from?: string, to?: string, extras?: Record<string, string>): string {
  const qs = new URLSearchParams()
  qs.set('projectId', projectId)
  if (from) qs.set('from', from)
  if (to) qs.set('to', to)
  if (extras) for (const [k, v] of Object.entries(extras)) qs.set(k, v)
  return `?${qs.toString()}`
}

export function getWebhookSummary(
  projectId: string,
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<WebhookDeliveryOverview> {
  return get<WebhookDeliveryOverview>(`/analytics/webhooks/summary${buildQs(projectId, from, to)}`, init)
}

export function getWebhookTimeseries(
  projectId: string,
  groupBy: WebhookGranularity = 'day',
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<WebhookDeliveryTimeseriesBucket[]> {
  return get<WebhookDeliveryTimeseriesBucket[]>(
    `/analytics/webhooks/timeseries${buildQs(projectId, from, to, { groupBy })}`,
    init,
  )
}
