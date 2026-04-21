import { Navigate, Outlet } from 'react-router'
import { useUserStore } from '../stores/user'

export function AuthGuard() {
  const userId = useUserStore((s) => s.userId)
  if (!userId) return <Navigate to="/login" replace />
  return <Outlet />
}
