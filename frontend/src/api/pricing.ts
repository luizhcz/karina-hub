import { get, post, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface ModelPricing {
  id: number
  modelId: string
  provider: string
  pricePerInputToken: number
  pricePerOutputToken: number
  currency: string
  effectiveFrom: string
  effectiveTo?: string
}

export interface CreateModelPricingRequest {
  modelId: string
  provider: string
  pricePerInputToken: number
  pricePerOutputToken: number
  currency?: string
  effectiveFrom: string
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  all: ['model-pricing'] as const,
  detail: (id: number) => ['model-pricing', id] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

// Backend retorna paginado `{ items, total, page, pageSize }` — extrai items para
// preservar a interface de array que todos os callers (CostDashboardPage, WorkflowCostPage,
// ProjectCostPage, ModelPricingPage) já consomem.
interface ModelPricingPage {
  items: ModelPricing[]
  total: number
  page: number
  pageSize: number
}

export const getModelPricings = async (): Promise<ModelPricing[]> => {
  const page = await get<ModelPricingPage>('/admin/model-pricing', { pageSize: 200 })
  return page.items
}
export const getModelPricing = (id: number) => get<ModelPricing>(`/admin/model-pricing/${id}`)
export const createModelPricing = (body: CreateModelPricingRequest) => post<ModelPricing>('/admin/model-pricing', body)
export const deleteModelPricing = (id: number) => del(`/admin/model-pricing/${id}`)
export const refreshPricingView = () => post<void>('/admin/model-pricing/refresh-view')

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useModelPricings() {
  return useQuery({ queryKey: KEYS.all, queryFn: getModelPricings })
}

export function useModelPricing(id: number, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getModelPricing(id), enabled })
}

export function useCreateModelPricing() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createModelPricing,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useDeleteModelPricing() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteModelPricing,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useRefreshPricingView() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: refreshPricingView,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}
