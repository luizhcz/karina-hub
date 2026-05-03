import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

export interface PredefinedModel {
  id: string
  displayName: string
  description: string
  provider: string
  clientType: string | null
  endpoint: string | null
  deploymentName: string
  defaultTemperature: number | null
  defaultMaxTokens: number | null
  enabled: boolean
  createdAt: string
  updatedAt: string
}

export interface CreatePredefinedModelRequest {
  id: string
  displayName: string
  description: string
  provider: string
  clientType: string | null
  endpoint: string | null
  deploymentName: string
  defaultTemperature: number | null
  defaultMaxTokens: number | null
  enabled: boolean
}

export interface UpdatePredefinedModelRequest extends Omit<CreatePredefinedModelRequest, 'id'> {
  expectedUpdatedAt: string
}

export const KEYS = {
  publicAll: ['predefined-models', 'public'] as const,
  adminAll: ['predefined-models', 'admin'] as const,
  adminDetail: (id: string) => ['predefined-models', 'admin', id] as const,
}

// Endpoint público — usado pelo AgentForm pra popular dropdown.
export const getPublicPresets = () => get<PredefinedModel[]>('/predefined-models')
// Endpoints admin — usados pela página de CRUD.
export const getAdminPresets = (includeDisabled = true) =>
  get<PredefinedModel[]>('/admin/predefined-models', { includeDisabled })
export const getAdminPreset = (id: string) =>
  get<PredefinedModel>(`/admin/predefined-models/${id}`)
export const createPreset = (body: CreatePredefinedModelRequest) =>
  post<PredefinedModel>('/admin/predefined-models', body)
export const updatePreset = (id: string, body: UpdatePredefinedModelRequest) =>
  put<PredefinedModel>(`/admin/predefined-models/${id}`, body)
export const deletePreset = (id: string) => del(`/admin/predefined-models/${id}`)

export function usePublicPresets() {
  return useQuery({ queryKey: KEYS.publicAll, queryFn: getPublicPresets })
}

export function useAdminPresets(includeDisabled = true) {
  return useQuery({
    queryKey: [...KEYS.adminAll, { includeDisabled }],
    queryFn: () => getAdminPresets(includeDisabled),
  })
}

export function useAdminPreset(id: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.adminDetail(id),
    queryFn: () => getAdminPreset(id),
    enabled,
  })
}

export function useCreatePreset() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createPreset,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: KEYS.adminAll })
      qc.invalidateQueries({ queryKey: KEYS.publicAll })
    },
  })
}

export function useUpdatePreset() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdatePredefinedModelRequest }) =>
      updatePreset(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.adminAll })
      qc.invalidateQueries({ queryKey: KEYS.adminDetail(id) })
      qc.invalidateQueries({ queryKey: KEYS.publicAll })
    },
  })
}

export function useDeletePreset() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deletePreset,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: KEYS.adminAll })
      qc.invalidateQueries({ queryKey: KEYS.publicAll })
    },
  })
}
