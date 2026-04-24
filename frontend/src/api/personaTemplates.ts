import { del, get, post } from './client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { UserType } from './personas'

/**
 * Template de prompt de persona persistido em aihub.persona_prompt_templates.
 * Scope embute userType: <c>global:cliente</c> | <c>global:admin</c> |
 * <c>agent:{id}:cliente</c> | <c>agent:{id}:admin</c>. Cache L1 (60s) +
 * Redis (5min) na camada C#; invalidação via Upsert/Delete.
 */
export interface PersonaPromptTemplate {
  id: number
  scope: string
  name: string
  template: string
  createdAt: string
  updatedAt: string
  /** Aponta pra version ativa no histórico append-only. */
  activeVersionId: string | null
}

/** Entrada no histórico append-only de versions de um template. */
export interface PersonaPromptTemplateVersion {
  id: number
  templateId: number
  versionId: string
  template: string
  createdAt: string
  createdBy: string | null
  changeReason: string | null
}

export interface PersonaPromptTemplateVersionsResponse {
  template: PersonaPromptTemplate
  versions: PersonaPromptTemplateVersion[]
  activeVersionId: string | null
}

export interface PersonaPromptTemplateListResponse {
  items: PersonaPromptTemplate[]
  placeholders: {
    client: string[]
    admin: string[]
  }
}

export interface PersonaPromptTemplateUpsertRequest {
  scope: string
  name: string
  template: string
}

export interface PersonaClientPreviewSample {
  clientName?: string | null
  suitabilityLevel?: string | null
  suitabilityDescription?: string | null
  businessSegment?: string | null
  country?: string | null
  isOffshore: boolean
}

export interface PersonaAdminPreviewSample {
  username?: string | null
  partnerType?: string | null
  segments?: string[]
  institutions?: string[]
  isInternal: boolean
  isWm: boolean
  isMaster: boolean
  isBroker: boolean
}

export interface PersonaPromptTemplatePreviewRequest {
  template: string
  userType: UserType
  client?: PersonaClientPreviewSample
  admin?: PersonaAdminPreviewSample
}

export interface PersonaPromptTemplatePreviewResponse {
  rendered: string | null
  // sample é serializado como o subtipo da persona — usamos `unknown` pra
  // não amarrar a UI à forma exata do back; mostramos só `rendered`.
  sample: unknown
}

export const PERSONA_TEMPLATE_KEYS = {
  all: ['persona-templates'] as const,
  detail: (id: number) => ['persona-templates', id] as const,
}

/**
 * Deduz o userType a partir do scope (sufixo `:cliente` ou `:admin`).
 * Default: 'cliente' (mantém experiência conservadora quando scope for
 * formato novo que ainda não foi atualizado).
 */
export function userTypeFromScope(scope: string): UserType {
  if (scope.endsWith(':admin')) return 'admin'
  return 'cliente'
}

export const getPersonaTemplates = () =>
  get<PersonaPromptTemplateListResponse>('/admin/persona-templates')

export const getPersonaTemplate = (id: number) =>
  get<PersonaPromptTemplate>(`/admin/persona-templates/${id}`)

export const upsertPersonaTemplate = (body: PersonaPromptTemplateUpsertRequest) =>
  post<PersonaPromptTemplate>('/admin/persona-templates', body)

export const deletePersonaTemplate = (id: number) =>
  del(`/admin/persona-templates/${id}`)

export const previewPersonaTemplate = (body: PersonaPromptTemplatePreviewRequest) =>
  post<PersonaPromptTemplatePreviewResponse>('/admin/persona-templates/preview', body)

export const getPersonaTemplateVersions = (id: number) =>
  get<PersonaPromptTemplateVersionsResponse>(`/admin/persona-templates/${id}/versions`)

export const rollbackPersonaTemplate = (id: number, versionId: string) =>
  post<PersonaPromptTemplate>(
    `/admin/persona-templates/${id}/rollback?versionId=${encodeURIComponent(versionId)}`,
  )

export function usePersonaTemplates() {
  return useQuery({
    queryKey: PERSONA_TEMPLATE_KEYS.all,
    queryFn: getPersonaTemplates,
  })
}

export function usePersonaTemplate(id: number | undefined) {
  return useQuery({
    queryKey: id !== undefined ? PERSONA_TEMPLATE_KEYS.detail(id) : ['persona-templates', 'none'],
    queryFn: () => getPersonaTemplate(id as number),
    enabled: id !== undefined && Number.isFinite(id),
  })
}

export function useUpsertPersonaTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: upsertPersonaTemplate,
    onSuccess: (saved) => {
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_KEYS.all })
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_KEYS.detail(saved.id) })
    },
  })
}

export function useDeletePersonaTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deletePersonaTemplate,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_KEYS.all })
    },
  })
}

export const PERSONA_TEMPLATE_VERSION_KEYS = {
  list: (templateId: number) => ['persona-templates', templateId, 'versions'] as const,
}

export function usePersonaTemplateVersions(templateId: number | undefined) {
  return useQuery({
    queryKey:
      templateId !== undefined
        ? PERSONA_TEMPLATE_VERSION_KEYS.list(templateId)
        : ['persona-templates', 'versions', 'none'],
    queryFn: () => getPersonaTemplateVersions(templateId as number),
    enabled: templateId !== undefined && Number.isFinite(templateId),
  })
}

export function useRollbackPersonaTemplate() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, versionId }: { id: number; versionId: string }) =>
      rollbackPersonaTemplate(id, versionId),
    onSuccess: (rolled) => {
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_KEYS.all })
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_KEYS.detail(rolled.id) })
      qc.invalidateQueries({ queryKey: PERSONA_TEMPLATE_VERSION_KEYS.list(rolled.id) })
    },
  })
}
