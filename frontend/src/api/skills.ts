import { get, put, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


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


export const KEYS = {
  all: ['skills'] as const,
  detail: (id: string) => ['skills', id] as const,
  versions: (id: string) => ['skills', id, 'versions'] as const,
}


// Backend retorna paginado `{ items, total, page, pageSize }` — extrai items para
// manter a interface de array que os callers esperam (SkillsListPage, AgentForm skill picker).
interface SkillPage {
  items: Skill[]
  total: number
  page: number
  pageSize: number
}

export const getSkills = async (): Promise<Skill[]> => {
  const page = await get<SkillPage>('/skills', { pageSize: 200 })
  return page.items
}
export const getSkill = (id: string) => get<Skill>(`/skills/${id}`)
export const updateSkill = (id: string, body: UpdateSkillRequest) => put<Skill>(`/skills/${id}`, body)
export const deleteSkill = (id: string) => del(`/skills/${id}`)
export const getSkillVersions = (id: string) => get<SkillVersion[]>(`/skills/${id}/versions`)


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
