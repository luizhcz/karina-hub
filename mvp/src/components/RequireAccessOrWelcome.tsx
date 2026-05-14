import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router'
import { useMe } from '../stores/me'

/**
 * Envolve rotas que dependem de um projeto vinculado. Caminhos:
 *   - Carregando /me: renderiza nada (evita flicker).
 *   - Admin: passa direto (admin enxerga tudo, mesmo sem vínculo explícito).
 *   - Non-admin com projects vazio: redireciona pra /bem-vindo.
 *   - Caso contrário: renderiza filhos.
 *
 * Rotas admin-only (ex.: /admin/usuarios) não usam este wrapper — quem
 * gateia elas é o próprio elemento da rota ou o AdminGate do backend
 * via 403.
 */
export function RequireAccessOrWelcome({ children }: { children: ReactNode }) {
  const me = useMe()
  const location = useLocation()

  if (me === null) return null

  if (!me.isAdmin && me.projects.length === 0) {
    return <Navigate to="/bem-vindo" replace state={{ from: location.pathname }} />
  }

  return <>{children}</>
}
