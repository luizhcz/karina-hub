import { get, post, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface DocumentIntelligencePricing {
  id: number
  modelId: string
  provider: string
  pricePerPage: number
  currency: string
  effectiveFrom: string
  effectiveTo?: string
}

export interface CreateDocumentIntelligencePricingRequest {
  modelId: string
  provider: string
  pricePerPage: number
  currency?: string
  effectiveFrom: string
}

export interface DocumentIntelligenceUsageSummary {
  totalJobs: number
  succeededJobs: number
  cachedJobs: number
  failedJobs: number
  totalPages: number
  totalCostUsd: number
}

export interface DocumentIntelligenceUsageByDay {
  day: string
  jobCount: number
  pages: number
  costUsd: number
}

export interface DocumentIntelligenceUsageByModel {
  model: string
  jobCount: number
  pages: number
  costUsd: number
}

export interface DocumentIntelligenceUsageResponse {
  from: string
  to: string
  summary: DocumentIntelligenceUsageSummary
  byDay: DocumentIntelligenceUsageByDay[]
  byModel: DocumentIntelligenceUsageByModel[]
}

export interface DocumentIntelligenceJobSummary {
  jobId: string
  conversationId: string
  userId: string
  model: string
  status: string
  pageCount: number | null
  costUsd: number | null
  durationMs: number | null
  createdAt: string
}

export interface DocumentIntelligenceJobsResponse {
  from: string
  to: string
  items: DocumentIntelligenceJobSummary[]
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const DI_KEYS = {
  pricingAll: ['di-pricing'] as const,
  pricingDetail: (id: number) => ['di-pricing', id] as const,
  usage: (from?: string, to?: string) => ['di-usage', from ?? 'default', to ?? 'default'] as const,
  jobs: (from?: string, to?: string, limit?: number) =>
    ['di-jobs', from ?? 'default', to ?? 'default', limit ?? 50] as const,
}

// ── Pricing (CRUD) ───────────────────────────────────────────────────────────

interface DocumentIntelligencePricingPage {
  items: DocumentIntelligencePricing[]
  total: number
  page: number
  pageSize: number
}

export const getDocumentIntelligencePricings = async (): Promise<DocumentIntelligencePricing[]> => {
  const page = await get<DocumentIntelligencePricingPage>(
    '/admin/document-intelligence/pricing',
    { pageSize: 200 },
  )
  return page.items
}

export const getDocumentIntelligencePricing = (id: number) =>
  get<DocumentIntelligencePricing>(`/admin/document-intelligence/pricing/${id}`)

export const createDocumentIntelligencePricing = (body: CreateDocumentIntelligencePricingRequest) =>
  post<DocumentIntelligencePricing>('/admin/document-intelligence/pricing', body)

export const deleteDocumentIntelligencePricing = (id: number) =>
  del(`/admin/document-intelligence/pricing/${id}`)

// ── Usage (read-only) ────────────────────────────────────────────────────────

export const getDocumentIntelligenceUsage = (from?: string, to?: string) =>
  get<DocumentIntelligenceUsageResponse>('/admin/document-intelligence/usage', {
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
  })

export const getDocumentIntelligenceJobs = (from?: string, to?: string, limit: number = 50) =>
  get<DocumentIntelligenceJobsResponse>('/admin/document-intelligence/jobs', {
    ...(from ? { from } : {}),
    ...(to ? { to } : {}),
    limit,
  })

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useDocumentIntelligencePricings() {
  return useQuery({ queryKey: DI_KEYS.pricingAll, queryFn: getDocumentIntelligencePricings })
}

export function useDocumentIntelligenceUsage(from?: string, to?: string) {
  return useQuery({
    queryKey: DI_KEYS.usage(from, to),
    queryFn: () => getDocumentIntelligenceUsage(from, to),
  })
}

export function useDocumentIntelligenceJobs(from?: string, to?: string, limit: number = 50) {
  return useQuery({
    queryKey: DI_KEYS.jobs(from, to, limit),
    queryFn: () => getDocumentIntelligenceJobs(from, to, limit),
  })
}

export function useCreateDocumentIntelligencePricing() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createDocumentIntelligencePricing,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: DI_KEYS.pricingAll })
    },
  })
}

export function useDeleteDocumentIntelligencePricing() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteDocumentIntelligencePricing,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: DI_KEYS.pricingAll })
    },
  })
}
