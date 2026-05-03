import { get } from './client'

export interface Project {
  id: string
  name: string
  tenantId: string
  description?: string | null
  createdAt: string
  updatedAt: string
}

export function listProjects(): Promise<Project[]> {
  return get<Project[]>('/projects')
}
