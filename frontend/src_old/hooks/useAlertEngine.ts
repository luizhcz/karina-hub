import { useState, useEffect, useRef, type MutableRefObject } from 'react'
import type { MonitorAlert, GlobalTokenSummary, ThroughputResult } from '../types'
import type { ExecStats } from '../components/ExecutionsMonitor'

interface AlertEngineInput {
  execStats: ExecStats | null
  tokenSummary: GlobalTokenSummary | null
  throughput: ThroughputResult | null
  eventsRef: MutableRefObject<{ type: string; payload: unknown; ts: number }[]>
}

const TOKEN_BUDGET_PER_HOUR = 500_000
const EVAL_INTERVAL_MS = 5_000 // evaluate alerts every 5s, not on every data change

export function useAlertEngine({ execStats, tokenSummary, throughput, eventsRef }: AlertEngineInput): MonitorAlert[] {
  const [alerts, setAlerts] = useState<MonitorAlert[]>([])
  const dismissedRef = useRef<Set<string>>(new Set())
  const pendingQueueCountRef = useRef(0)

  // Store latest data in refs so the interval always reads current values
  const execStatsRef = useRef(execStats)
  execStatsRef.current = execStats
  const tokenSummaryRef = useRef(tokenSummary)
  tokenSummaryRef.current = tokenSummary
  const throughputRef = useRef(throughput)
  throughputRef.current = throughput

  useEffect(() => {
    function evaluate() {
      const stats = execStatsRef.current
      const summary = tokenSummaryRef.current
      const tp = throughputRef.current
      const events = eventsRef.current
      const now = Date.now()
      const newAlerts: MonitorAlert[] = []

      // 1. High failure rate >15%
      if (stats) {
        const finalized = stats.completed + stats.failed
        if (finalized > 0) {
          const failRate = (stats.failed / finalized) * 100
          if (failRate > 15) {
            newAlerts.push({
              id: 'high_failure_rate', severity: 'CRITICAL',
              title: 'Taxa de falha elevada',
              message: `${failRate.toFixed(0)}% das execucoes estao falhando (${stats.failed}/${finalized})`,
              triggeredAt: now, value: failRate, threshold: 15,
            })
          }
        }

        // 2/3. Queue congested
        if (stats.pending > 50) {
          pendingQueueCountRef.current++
          if (pendingQueueCountRef.current >= 3) {
            newAlerts.push({
              id: 'queue_congested_crit', severity: 'CRITICAL',
              title: 'Fila critica',
              message: `${stats.pending} execucoes pendentes na fila`,
              triggeredAt: now, value: stats.pending, threshold: 50,
            })
          }
        } else if (stats.pending > 20) {
          pendingQueueCountRef.current++
          if (pendingQueueCountRef.current >= 3) {
            newAlerts.push({
              id: 'queue_congested_warn', severity: 'WARNING',
              title: 'Fila congestionada',
              message: `${stats.pending} execucoes pendentes na fila`,
              triggeredAt: now, value: stats.pending, threshold: 20,
            })
          }
        } else {
          pendingQueueCountRef.current = 0
        }
      }

      // 4. Degraded latency
      if (tp && tp.buckets.length > 0) {
        const latest = tp.buckets[tp.buckets.length - 1]
        if (latest.avgDurationMs > 30_000) {
          newAlerts.push({
            id: 'degraded_latency', severity: 'WARNING',
            title: 'Latencia degradada',
            message: `Avg ${(latest.avgDurationMs / 1000).toFixed(1)}s (threshold: 30s)`,
            triggeredAt: now, value: latest.avgDurationMs, threshold: 30_000,
          })
        }
      }

      // 5. LLM rate limit
      const oneMinAgo = now - 60_000
      const rateLimitErrors = events.filter(e => {
        if (e.ts < oneMinAgo || e.type !== 'error') return false
        const msg = String((e.payload as Record<string, unknown>)?.message ?? '').toLowerCase()
        return msg.includes('rate limit') || msg.includes('429') || msg.includes('too many')
      })
      if (rateLimitErrors.length > 5) {
        newAlerts.push({
          id: 'llm_rate_limit', severity: 'CRITICAL',
          title: 'Rate limit LLM',
          message: `${rateLimitErrors.length} erros de rate limit no ultimo minuto`,
          triggeredAt: now, value: rateLimitErrors.length, threshold: 5,
        })
      }

      // 6. Token budget exceeded
      if (summary && summary.totalTokens > TOKEN_BUDGET_PER_HOUR) {
        newAlerts.push({
          id: 'token_budget_exceeded', severity: 'CRITICAL',
          title: 'Budget de tokens excedido',
          message: `${summary.totalTokens.toLocaleString()} tokens (budget: ${TOKEN_BUDGET_PER_HOUR.toLocaleString()}/h)`,
          triggeredAt: now, value: summary.totalTokens, threshold: TOKEN_BUDGET_PER_HOUR,
        })
      }

      // Filter out dismissed alerts
      const filtered = newAlerts.filter(a => !dismissedRef.current.has(a.id))

      // Auto-clear dismissed if condition resolved
      const activeIds = new Set(newAlerts.map(a => a.id))
      for (const id of dismissedRef.current) {
        if (!activeIds.has(id)) {
          dismissedRef.current.delete(id)
        }
      }

      setAlerts(prev => {
        // Only update state if alerts actually changed (avoid unnecessary re-renders)
        const prevIds = prev.map(a => a.id).sort().join(',')
        const newIds = filtered.map(a => a.id).sort().join(',')
        if (prevIds === newIds) return prev
        return filtered
      })
    }

    // Run immediately then on interval
    evaluate()
    const timer = setInterval(evaluate, EVAL_INTERVAL_MS)
    return () => clearInterval(timer)
  }, [eventsRef])

  return alerts
}
