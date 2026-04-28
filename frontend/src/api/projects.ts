import { get, post, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export interface ProviderCredentialsResponse {
  apiKeySet: boolean
  /** Referência AWS Secrets Manager (`secret://aws/...`) quando aplicável; null em legacy DPAPI. */
  secretRef?: string | null
  /** True quando a credencial está em formato DPAPI legacy — UI mostra nudge de recadastro. */
  legacyDpapi?: boolean
  endpoint?: string
  keyVersion?: string
}

export interface ProjectLlmConfigResponse {
  credentials?: Record<string, ProviderCredentialsResponse>
  defaultModel?: string
  defaultProvider?: string
}

export interface FoundryEvaluationSettings {
  enabled?: boolean
  endpoint?: string | null
  modelDeployment?: string | null
  // GET retorna "***" quando literal e prefixo "secret://..." quando referência.
  apiKeyRef?: string | null
  // Obrigatório só para evaluators Safety (Violence/Sexual/SelfHarm/HateAndUnfairness).
  // Auth via DefaultAzureCredential.
  projectEndpoint?: string | null
}

export interface EvaluationProjectSettings {
  foundry?: FoundryEvaluationSettings | null
}

export interface ProjectSettings {
  schemaVersion?: number
  defaultProvider?: string | null
  defaultModel?: string | null
  defaultTemperature?: number | null
  maxTokensPerDay?: number | null
  maxCostUsdPerDay?: number | null
  maxConcurrentExecutions?: number | null
  maxRequestsPerMinute?: number | null
  maxConversationsPerUser?: number | null
  hitlEnabled?: boolean
  backgroundResponsesEnabled?: boolean
  maxSandboxTokensPerDay?: number | null
  evaluation?: EvaluationProjectSettings | null
}

export interface Project {
  id: string
  name: string
  tenantId?: string
  description?: string
  settings?: ProjectSettings
  llmConfig?: ProjectLlmConfigResponse
  createdAt?: string
  updatedAt?: string
}

export interface ProviderCredentialsInput {
  apiKey?: string
  endpoint?: string
}

export interface ProjectLlmConfigInput {
  credentials?: Record<string, ProviderCredentialsInput>
  defaultModel?: string
  defaultProvider?: string
}

// Mesmo shape de ProjectSettings do response — input total (PUT
// substitui o objeto inteiro, então o caller precisa ler current
// settings, mesclar mudanças, e enviar de volta).
export type ProjectSettingsInput = ProjectSettings

export interface CreateProjectRequest {
  name: string
  description?: string
  settings?: ProjectSettingsInput
  llmConfig?: ProjectLlmConfigInput
}

export interface UpdateProjectRequest {
  name?: string
  description?: string
  settings?: ProjectSettingsInput
  llmConfig?: ProjectLlmConfigInput
}

export interface ProjectStats {
  projectId: string
  agentCount: number
  workflowCount: number
  executionCount: number
}


export const KEYS = {
  all: ['projects'] as const,
  detail: (id: string) => ['projects', id] as const,
  stats: (id: string) => ['projects', id, 'stats'] as const,
}


export const getProjects = () => get<Project[]>('/projects')
export const getProject = (id: string) => get<Project>(`/projects/${id}`)
export const createProject = (body: CreateProjectRequest) => post<Project>('/projects', body)
export const updateProject = (id: string, body: UpdateProjectRequest) => put<Project>(`/projects/${id}`, body)
export const deleteProject = (id: string) => del(`/projects/${id}`)
export const getProjectStats = (id: string) => get<ProjectStats>(`/projects/${id}/stats`)


export function useProjects() {
  return useQuery({ queryKey: KEYS.all, queryFn: getProjects })
}

export function useProject(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getProject(id), enabled })
}

export function useProjectStats(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.stats(id), queryFn: () => getProjectStats(id), enabled })
}

export function useCreateProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createProject,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useUpdateProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateProjectRequest }) => updateProject(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteProject() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteProject,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}
