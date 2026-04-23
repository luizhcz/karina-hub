import { get, post } from './client'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export type UserType = 'cliente' | 'admin'

/** Campos comuns a toda persona. */
interface BasePersona {
  userId: string
  userType: UserType
}

/** Persona de cliente final (investidor). */
export interface ClientPersona extends BasePersona {
  userType: 'cliente'
  clientName: string | null
  suitabilityLevel: string | null
  suitabilityDescription: string | null
  businessSegment: string | null
  country: string | null
  isOffshore: boolean
}

/** Persona de admin (assessor/gestor/consultor/padrão). */
export interface AdminPersona extends BasePersona {
  userType: 'admin'
  username: string | null
  partnerType: string | null
  segments: string[]
  institutions: string[]
  isInternal: boolean
  isWm: boolean
  isMaster: boolean
  isBroker: boolean
}

/**
 * Union discriminada por <c>userType</c>. Consumidores da UI devem narrow
 * via `persona.userType === 'cliente'` antes de acessar campos específicos.
 */
export type UserPersona = ClientPersona | AdminPersona

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
