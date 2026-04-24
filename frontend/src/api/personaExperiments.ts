import { del, get, post } from './client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'


/**
 * Experiment A/B de templates. Scope é o mesmo string de template
 * (project:{pid}:*, agent:{aid}:*, global:*). Variants apontam pra
 * VersionIds (UUID) em persona_prompt_template_versions — snapshots
 * imutáveis.
 */
export interface PersonaPromptExperiment {
  id: number
  projectId: string
  scope: string
  name: string
  variantAVersionId: string
  variantBVersionId: string
  /** 0-100. % de tráfego pra B. A = 100 - B. */
  trafficSplitB: number
  metric: string
  startedAt: string
  endedAt: string | null
  createdBy: string | null
  isActive: boolean
}

export interface PersonaPromptExperimentCreateRequest {
  scope: string
  name: string
  variantAVersionId: string
  variantBVersionId: string
  trafficSplitB: number
  metric: string
}

export interface ExperimentVariantResult {
  variant: string // 'A' | 'B'
  sampleCount: number
  totalTokens: number
  cachedTokens: number
  avgTotalTokens: number
  avgDurationMs: number
}

export interface PersonaPromptExperimentResultsResponse {
  experiment: PersonaPromptExperiment
  results: ExperimentVariantResult[]
}


export const PERSONA_EXPERIMENT_KEYS = {
  all: ['persona-experiments'] as const,
  detail: (id: number) => ['persona-experiments', id] as const,
  results: (id: number) => ['persona-experiments', id, 'results'] as const,
}


export const getPersonaExperiments = () =>
  get<PersonaPromptExperiment[]>('/admin/persona-experiments')

export const getPersonaExperiment = (id: number) =>
  get<PersonaPromptExperiment>(`/admin/persona-experiments/${id}`)

export const getPersonaExperimentResults = (id: number) =>
  get<PersonaPromptExperimentResultsResponse>(
    `/admin/persona-experiments/${id}/results`,
  )

export const createPersonaExperiment = (body: PersonaPromptExperimentCreateRequest) =>
  post<PersonaPromptExperiment>('/admin/persona-experiments', body)

export const endPersonaExperiment = (id: number) =>
  post<void>(`/admin/persona-experiments/${id}/end`)

export const deletePersonaExperiment = (id: number) =>
  del(`/admin/persona-experiments/${id}`)


export function usePersonaExperiments() {
  return useQuery({
    queryKey: PERSONA_EXPERIMENT_KEYS.all,
    queryFn: getPersonaExperiments,
  })
}

export function usePersonaExperimentResults(id: number | undefined) {
  return useQuery({
    queryKey:
      id !== undefined
        ? PERSONA_EXPERIMENT_KEYS.results(id)
        : ['persona-experiments', 'results', 'none'],
    queryFn: () => getPersonaExperimentResults(id as number),
    enabled: id !== undefined && Number.isFinite(id),
  })
}

export function useCreatePersonaExperiment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createPersonaExperiment,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PERSONA_EXPERIMENT_KEYS.all })
    },
  })
}

export function useEndPersonaExperiment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: endPersonaExperiment,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PERSONA_EXPERIMENT_KEYS.all })
    },
  })
}

export function useDeletePersonaExperiment() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deletePersonaExperiment,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PERSONA_EXPERIMENT_KEYS.all })
    },
  })
}
