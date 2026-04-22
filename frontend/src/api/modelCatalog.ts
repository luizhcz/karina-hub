import { get, post, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface ModelCatalog {
  id: string
  provider: string
  displayName: string
  description?: string
  contextWindow?: number
  capabilities: string[]
  isActive: boolean
  createdAt: string
  updatedAt: string
}

export interface UpsertModelCatalogRequest {
  id: string
  provider: string
  displayName: string
  description?: string
  contextWindow?: number
  capabilities?: string[]
  isActive?: boolean
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const MODEL_CATALOG_KEYS = {
  all: (provider?: string) => ['model-catalog', provider ?? 'all'] as const,
  detail: (id: string, provider: string) => ['model-catalog', provider, id] as const,
}

// ── Raw API Functions ─────────────────────────────────────────────────────────

// Backend retorna paginado `{ items, total, page, pageSize }` — extrai items para
// manter a interface de array usada por ModelCatalogPage.
interface ModelCatalogPage {
  items: ModelCatalog[]
  total: number
  page: number
  pageSize: number
}

export const getModelCatalog = async (provider?: string, activeOnly = true): Promise<ModelCatalog[]> => {
  const url = `/model-catalog${provider ? `?provider=${provider}` : ''}${activeOnly ? (provider ? '&' : '?') + 'activeOnly=true' : ''}`
  const page = await get<ModelCatalogPage>(url)
  return page.items
}

export const upsertModelCatalog = (body: UpsertModelCatalogRequest) =>
  post<ModelCatalog>('/model-catalog', body)

export const deactivateModel = (provider: string, id: string) =>
  del(`/model-catalog/${provider}/${id}`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useModelCatalog(provider?: string, activeOnly = true) {
  return useQuery({
    queryKey: MODEL_CATALOG_KEYS.all(provider),
    queryFn: () => getModelCatalog(provider, activeOnly),
  })
}

export function useUpsertModelCatalog() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: upsertModelCatalog,
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['model-catalog'] }) },
  })
}

export function useDeactivateModel() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ provider, id }: { provider: string; id: string }) => deactivateModel(provider, id),
    onSuccess: () => { qc.invalidateQueries({ queryKey: ['model-catalog'] }) },
  })
}
