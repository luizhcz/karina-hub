import { get } from './client'

// Espelha endpoint em
// src/EfsAiHub.Host.Api/Controllers/BackgroundServicesController.cs.
// Read-only — apenas lista o registry de IHostedServices do compose root.

export type BackgroundLifecycle = 'Continuous' | 'OneTime' | string

export interface BackgroundServiceItem {
  name: string
  description: string
  lifecycle: BackgroundLifecycle
  /** Segundos entre execuções. Null pra services event-driven (NodePersistence, lifecycle=Continuous mas sem timer). */
  intervalSeconds?: number | null
  typeName: string
}

export interface BackgroundServicesResponse {
  items: BackgroundServiceItem[]
  total: number
}

export function listBackgroundServices(init?: RequestInit): Promise<BackgroundServicesResponse> {
  return get<BackgroundServicesResponse>('/admin/background-services', init)
}
