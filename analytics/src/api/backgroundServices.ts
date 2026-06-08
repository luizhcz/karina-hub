import { get } from './client'

// Espelha endpoint em
// src/EfsAiHub.Host.Api/Controllers/BackgroundServicesController.cs.
// Read-only — combina registry estático + heartbeats em runtime.

export type BackgroundLifecycle = 'Continuous' | 'OneTime' | string

export type BackgroundCategory =
  | 'Bootstrap'
  | 'Persistence'
  | 'Dispatcher'
  | 'Recovery'
  | 'Evaluation'
  | 'Cleanup'
  | 'Messaging'
  | 'Guards'
  | string

/**
 * Heartbeat per-pod do serviço. Quando o pod nunca executou (ou ainda não chamou
 * Started), o backend devolve `heartbeat: null` — significa "registrado mas
 * sem evidência de execução neste pod".
 */
export interface BackgroundServiceHeartbeat {
  startedAtUtc: string | null
  lastTickAtUtc: string | null
  lastSuccessAtUtc: string | null
  lastErrorAtUtc: string | null
  lastErrorMessage: string | null
  tickCount: number
  errorCount: number
}

export interface BackgroundServiceItem {
  name: string
  description: string
  lifecycle: BackgroundLifecycle
  category: BackgroundCategory
  /** Segundos entre execuções esperadas. Null pra services event-driven. */
  intervalSeconds?: number | null
  typeName: string
  heartbeat: BackgroundServiceHeartbeat | null
}

export interface BackgroundServicesResponse {
  /** ISO timestamp do momento em que o processo do backend subiu. */
  processStartedAtUtc: string
  /** ISO timestamp do servidor no momento do GET — base estável pra calcular relativos. */
  nowUtc: string
  items: BackgroundServiceItem[]
  total: number
}

export function listBackgroundServices(init?: RequestInit): Promise<BackgroundServicesResponse> {
  return get<BackgroundServicesResponse>('/admin/background-services', init)
}
