import { get } from './client'
import { useQuery } from '@tanstack/react-query'

/**
 * Background service registrado em BackgroundServiceRegistry no backend.
 * lifecycle: "OneTime" roda no startup; "Continuous" é long-running (timer ou listener).
 * intervalSeconds: presente só em serviços timer-based (ex: AuditRetention 24h).
 */
export interface BackgroundServiceInfo {
  name: string
  description?: string
  lifecycle: string
  intervalSeconds?: number
  typeName: string
}

export interface BackgroundServicesResponse {
  items: BackgroundServiceInfo[]
  total: number
}

export const KEYS = {
  all: ['admin', 'background-services'] as const,
}

export const getBackgroundServices = () =>
  get<BackgroundServicesResponse>('/admin/background-services')

export function useBackgroundServices() {
  return useQuery({
    queryKey: KEYS.all,
    queryFn: getBackgroundServices,
    staleTime: 60_000,
  })
}
