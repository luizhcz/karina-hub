import { get, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface Skill {
  id: string
  name: string
  description?: string
  type: string
  configuration?: Record<string, unknown>
  createdAt?: string
  updatedAt?: string
}

export interface UpdateSkillRequest {
  name?: string
  description?: string
  type?: string
  configuration?: Record<string, unknown>
}

export interface SkillVersion {
  versionId: string
  skillId: string
  createdAt: string
  description?: string
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  all: ['skills'] as const,
  detail: (id: string) => ['skills', id] as const,
  versions: (id: string) => ['skills', id, 'versions'] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const getSkills = () => get<Skill[]>('/skills')
export const getSkill = (id: string) => get<Skill>(`/skills/${id}`)
export const updateSkill = (id: string, body: UpdateSkillRequest) => put<Skill>(`/skills/${id}`, body)
export const deleteSkill = (id: string) => del(`/skills/${id}`)
export const getSkillVersions = (id: string) => get<SkillVersion[]>(`/skills/${id}/versions`)

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useSkills() {
  return useQuery({ queryKey: KEYS.all, queryFn: getSkills })
}

export function useSkill(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getSkill(id), enabled })
}

export function useSkillVersions(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.versions(id), queryFn: () => getSkillVersions(id), enabled })
}

export function useUpdateSkill() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateSkillRequest }) => updateSkill(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteSkill() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteSkill,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}
