import { get, post } from './client'

/**
 * API client da feature de ingestão URL → arquivo → workflow. Backend cobre:
 * - POST /api/aihub/ingestions      → cria job (Status='Queued')
 * - GET  /api/aihub/responses/{id}  → polling do estado terminal
 *
 * Headers de identidade (x-efs-user-profile-id, x-efs-permissions,
 * x-project-id) são injetados automaticamente pelo client.ts.
 */

export interface IngestionCallback {
  url: string
  hmacSecret?: string | null
  headers?: Record<string, string> | null
}

export interface IngestionSource {
  type: 'url'
  url: string
  headers?: Record<string, string> | null
}

export interface CreateIngestionInput {
  workflowId: string
  source: IngestionSource
  metadata?: Record<string, string> | null
  callback?: IngestionCallback | null
  idempotencyKey?: string | null
}

export interface IngestionAcceptedResponse {
  jobId: string
  workflowId: string | null
  status: string
  step: string | null
  createdAt: string
  updatedAt: string
  pollUrl: string
}

export type StandaloneJobStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'

export interface StandaloneJobResponse {
  jobId: string
  workflowId: string | null
  executionId: string | null
  status: StandaloneJobStatus
  step: string | null
  attempt: number
  output: string | null
  lastError: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
  updatedAt: string
  pollUrl: string
}

export function createIngestion(input: CreateIngestionInput): Promise<IngestionAcceptedResponse> {
  return post<IngestionAcceptedResponse>('/ingestions', input)
}

export function getStandaloneJob(jobId: string): Promise<StandaloneJobResponse> {
  return get<StandaloneJobResponse>(`/responses/${encodeURIComponent(jobId)}`)
}

export const TERMINAL_STATUSES: ReadonlySet<StandaloneJobStatus> = new Set(['Completed', 'Failed', 'Cancelled'])

export function isTerminal(status: StandaloneJobStatus): boolean {
  return TERMINAL_STATUSES.has(status)
}
