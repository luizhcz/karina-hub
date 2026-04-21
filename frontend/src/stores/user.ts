import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { UserType } from '../constants/identity'

export type { UserType }

interface UserStore {
  userId: string
  userType: UserType
  setUser: (userId: string, userType: UserType) => void
  logout: () => void
}

export const useUserStore = create<UserStore>()(
  persist(
    (set) => ({
      userId: '',
      userType: 'cliente',
      setUser: (userId, userType) => set({ userId, userType }),
      logout: () => set({ userId: '', userType: 'cliente' }),
    }),
    { name: 'efs-user' },
  ),
)
