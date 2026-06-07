import { get } from './client'

// Espelha DTOs em
// src/EfsAiHub.Core.Abstractions/Observability/IDocumentIntelligenceUsageQueries.cs
// e src/EfsAiHub.Core.Abstractions/Observability/DocumentIntelligencePricing.cs.
// Endpoints em
// src/EfsAiHub.Host.Api/Controllers/DocumentIntelligenceAdminController.cs.
//
// IMPORTANTE: rota `/admin/document-intelligence/*` é admin-gated e NÃO é
// project-scoped no backend (lê direto de document_extraction_jobs sem JOIN
// com conversations/projects). UI mostra "cross-projeto" no header pra que
// o usuário saiba.

export interface DiUsageSummary {
  totalJobs: number
  succeededJobs: number
  cachedJobs: number
  failedJobs: number
  totalPages: number
  totalCostUsd: number
}

export interface DiUsageByDay {
  /** YYYY-MM-DD (DateOnly do backend). */
  day: string
  jobCount: number
  pages: number
  costUsd: number
}

export interface DiUsageByModel {
  model: string
  jobCount: number
  pages: number
  costUsd: number
}

export interface DiUsageResponse {
  from: string
  to: string
  summary: DiUsageSummary
  byDay: DiUsageByDay[]
  byModel: DiUsageByModel[]
}

export interface DiJobSummary {
  jobId: string
  conversationId: string
  userId: string
  model: string
  status: string
  pageCount?: number | null
  costUsd?: number | null
  durationMs?: number | null
  createdAt: string
}

export interface DiJobsResponse {
  from: string
  to: string
  items: DiJobSummary[]
}

export interface DiPricing {
  id: number
  modelId: string
  provider: string
  pricePerPage: number
  currency: string
  effectiveFrom: string
  effectiveTo?: string | null
  createdAt: string
}

export interface DiPricingResponse {
  items: DiPricing[]
  total: number
  page: number
  pageSize: number
}

function qs(extras: Record<string, string | undefined>): string {
  const p = new URLSearchParams()
  for (const [k, v] of Object.entries(extras)) {
    if (v !== undefined && v !== '') p.set(k, v)
  }
  const s = p.toString()
  return s ? `?${s}` : ''
}

export function getDocumentIntelligenceUsage(
  from?: string,
  to?: string,
  init?: RequestInit,
): Promise<DiUsageResponse> {
  return get<DiUsageResponse>(
    `/admin/document-intelligence/usage${qs({ from, to })}`,
    init,
  )
}

export function getDocumentIntelligenceJobs(
  from?: string,
  to?: string,
  limit = 50,
  init?: RequestInit,
): Promise<DiJobsResponse> {
  return get<DiJobsResponse>(
    `/admin/document-intelligence/jobs${qs({ from, to, limit: String(limit) })}`,
    init,
  )
}

export function getDocumentIntelligencePricing(
  init?: RequestInit,
): Promise<DiPricingResponse> {
  return get<DiPricingResponse>(
    `/admin/document-intelligence/pricing?pageSize=50`,
    init,
  )
}
