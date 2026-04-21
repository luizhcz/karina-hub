import { get } from './client'
import { useQuery } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface EnumsResponse {
  orchestrationModes: string[]
  edgeTypes: string[]
  triggerTypes: string[]
  executionStatuses: string[]
  hitlStatuses: string[]
  middlewarePhases: string[]
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  enums: ['enums'] as const,
}

// ── Raw API Functions ────────────────────────────────────────────────────────

export const getEnums = () => get<EnumsResponse>('/enums')

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useEnums() {
  return useQuery({
    queryKey: KEYS.enums,
    queryFn: getEnums,
    staleTime: Infinity,
  })
}
