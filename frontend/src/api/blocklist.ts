import { get, put } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Domain types (espelha BlocklistSettings/Catalog do backend) ────────────────

export type BlocklistAction = 'Block' | 'Redact' | 'Warn'
export type BlocklistPatternType = 'Literal' | 'Regex' | 'BuiltIn'

export interface BlocklistGroupOverride {
  enabled: boolean
  actionOverride?: BlocklistAction | null
  disabledPatterns?: string[]
}

export interface BlocklistCustomPattern {
  id: string
  type: BlocklistPatternType
  pattern: string
  action: BlocklistAction
  wholeWord: boolean
  caseSensitive: boolean
}

export interface BlocklistSettings {
  enabled: boolean
  scanInput: boolean
  scanOutput: boolean
  replacement: string
  auditBlocks: boolean
  groups?: Record<string, BlocklistGroupOverride>
  customPatterns?: BlocklistCustomPattern[]
}

export interface BlocklistPatternGroup {
  id: string
  name: string
  description?: string
  version: number
}

export interface BlocklistPattern {
  id: string
  groupId: string
  type: BlocklistPatternType
  pattern: string
  validator: 'None' | 'Mod11' | 'Luhn'
  wholeWord: boolean
  caseSensitive: boolean
  defaultAction: BlocklistAction
  enabled: boolean
  version: number
}

export interface BlocklistCatalogResponse {
  version: number
  groups: BlocklistPatternGroup[]
  patterns: BlocklistPattern[]
}

export interface ProjectBlocklistResponse {
  projectId: string
  settings: BlocklistSettings
}

export interface BlocklistViolationRow {
  auditId: number
  detectedAt: string
  userId?: string
  agentId: string
  phase?: string
  category?: string
  patternId?: string
  action?: string
  contentHash?: string
  contextObfuscated?: string
}

// ── Request shape (PUT /api/projects/{id}/blocklist) ───────────────────────────

export type UpdateBlocklistRequest = BlocklistSettings

// ── Query keys ────────────────────────────────────────────────────────────────

export const KEYS = {
  catalog: ['blocklist', 'catalog'] as const,
  project: (id: string) => ['blocklist', 'project', id] as const,
  violations: (id: string, page: number, pageSize: number) =>
    ['blocklist', 'violations', id, page, pageSize] as const,
}

// ── Hooks ──────────────────────────────────────────────────────────────────────

export function useBlocklistCatalog(enabled = true) {
  return useQuery({
    queryKey: KEYS.catalog,
    queryFn: () => get<BlocklistCatalogResponse>('/admin/blocklist/catalog'),
    enabled,
    staleTime: 60_000, // catálogo muda raro (DBA-driven)
  })
}

export function useProjectBlocklist(projectId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.project(projectId),
    queryFn: () => get<ProjectBlocklistResponse>(`/projects/${projectId}/blocklist`),
    enabled: enabled && !!projectId,
  })
}

export function useUpdateProjectBlocklist() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ projectId, body }: { projectId: string; body: UpdateBlocklistRequest }) =>
      put<ProjectBlocklistResponse>(`/projects/${projectId}/blocklist`, body),
    onSuccess: (_d, { projectId }) => {
      qc.invalidateQueries({ queryKey: KEYS.project(projectId) })
    },
  })
}

export function useBlocklistViolations(projectId: string, page = 1, pageSize = 50, enabled = true) {
  return useQuery({
    queryKey: KEYS.violations(projectId, page, pageSize),
    queryFn: () => get<BlocklistViolationRow[]>(
      `/projects/${projectId}/blocklist/violations?page=${page}&pageSize=${pageSize}`,
    ),
    enabled: enabled && !!projectId,
  })
}
