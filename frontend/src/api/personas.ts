import { get, post } from './client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

/**
 * UserPersona resolvida pelo HttpPersonaProvider (API externa) com cache
 * Redis (5min) + in-memory (60s). Fonte de verdade é externa — este client
 * só consulta e invalida.
 */
export interface UserPersona {
  userId: string
  userType: string
  displayName: string | null
  segment: string | null
  riskProfile: string | null
  advisorId: string | null
}

export type UserType = 'cliente' | 'assessor'

// ── Query Keys ───────────────────────────────────────────────────────────────

export const PERSONA_KEYS = {
  detail: (userId: string, userType: string) => ['personas', userType, userId] as const,
}

// ── Raw API ──────────────────────────────────────────────────────────────────

export const getPersona = (userId: string, userType: UserType = 'cliente') =>
  get<UserPersona>(`/admin/personas/${encodeURIComponent(userId)}`, { userType })

export const invalidatePersona = (userId: string, userType: UserType = 'cliente') =>
  post<void>(
    `/admin/personas/${encodeURIComponent(userId)}/invalidate?userType=${encodeURIComponent(userType)}`,
  )

// ── Hooks ────────────────────────────────────────────────────────────────────

/**
 * Resolve persona sob demanda — `enabled` controla disparo (tipicamente
 * acionado por botão "Consultar" na UI, não automático).
 */
export function usePersona(userId: string, userType: UserType = 'cliente', enabled = false) {
  return useQuery({
    queryKey: PERSONA_KEYS.detail(userId, userType),
    queryFn: () => getPersona(userId, userType),
    enabled: enabled && userId.trim().length > 0,
    staleTime: 0,
    refetchOnWindowFocus: false,
  })
}

export function useInvalidatePersona() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ userId, userType }: { userId: string; userType: UserType }) =>
      invalidatePersona(userId, userType),
    onSuccess: (_d, { userId, userType }) => {
      qc.invalidateQueries({ queryKey: PERSONA_KEYS.detail(userId, userType) })
    },
  })
}
