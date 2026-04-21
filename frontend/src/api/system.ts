import { get } from './client'
import { useQuery } from '@tanstack/react-query'

// ── Types ────────────────────────────────────────────────────────────────────

export interface CircuitBreakerState {
  providerKey: string
  status: 'Closed' | 'Open' | 'HalfOpen'
  consecutiveFailures: number
  opensAt?: string
  halfOpenDeadline?: string
  isOperational: boolean
}

export interface CircuitBreakersResponse {
  circuitBreakers: CircuitBreakerState[]
}

export interface QueueStatus {
  name: string
  capacity: number
  pending: number
  active: number
}

export interface QueuesResponse {
  queues: QueueStatus[]
}

// ── Query Keys ───────────────────────────────────────────────────────────────

export const KEYS = {
  circuitBreakers: ['system', 'circuit-breakers'] as const,
  queues: ['system', 'queues'] as const,
}

// ── Hooks ────────────────────────────────────────────────────────────────────

export function useCircuitBreakers() {
  return useQuery({
    queryKey: KEYS.circuitBreakers,
    queryFn: () => get<CircuitBreakersResponse>('/system/health/circuit-breakers'),
    refetchInterval: 10_000,
  })
}

export function useQueues() {
  return useQuery({
    queryKey: KEYS.queues,
    queryFn: () => get<QueuesResponse>('/system/health/queues'),
    refetchInterval: 5_000,
  })
}
