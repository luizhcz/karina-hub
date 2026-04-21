import { create } from 'zustand'

interface ProjectStore {
  projectId: string
  projectName: string
  setProject: (id: string, name: string) => void
}

export const useProjectStore = create<ProjectStore>((set) => ({
  projectId: 'default',
  projectName: 'Default',
  setProject: (id, name) => set({ projectId: id, projectName: name }),
}))
