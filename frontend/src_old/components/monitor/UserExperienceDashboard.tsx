import { useMemo, useCallback, useRef } from 'react'
import { api } from '../../api'
import { usePolledData } from '../../hooks/usePolledData'
import { MetricCard } from './shared/MetricCard'
import { MiniBarChart } from './shared/MiniBarChart'
import type { WorkflowDef, WorkflowExecution, TimeRange } from '../../types'

function fmtDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  return `${Math.floor(s / 60)}m${Math.round(s % 60)}s`
}

function percentile(values: number[], p: number): number {
  if (values.length === 0) return 0
  const sorted = [...values].sort((a, b) => a - b)
  const idx = Math.ceil(sorted.length * p) - 1
  return sorted[Math.max(0, idx)]
}

interface Props {
  workflows: WorkflowDef[]
  timeRange: TimeRange
}

export function UserExperienceDashboard({ workflows }: Props) {
  // Identify Chat workflows
  const chatWorkflows = useMemo(
    () => workflows.filter(w => w.configuration?.inputMode === 'Chat'),
    [workflows]
  )

  // Use ref so the fetcher always reads latest chatWorkflows without dep instability
  const chatWfsRef = useRef(chatWorkflows)
  chatWfsRef.current = chatWorkflows
  const chatWfCount = chatWorkflows.length

  // Fetch recent executions from chat workflows
  const { data: chatExecs } = usePolledData<WorkflowExecution[]>(
    useCallback(async () => {
      const wfs = chatWfsRef.current
      if (wfs.length === 0) return []
      const results = await Promise.all(
        wfs.map(wf => api.getWorkflowExecutions(wf.id, undefined, 20))
      )
      return results.flat()
    }, []),
    15_000,
    [chatWfCount] // only re-init when count of chat workflows changes
  )

  const metrics = useMemo(() => {
    if (!chatExecs || chatExecs.length === 0) return null

    const running = chatExecs.filter(e => e.status === 'Running')
    const completed = chatExecs.filter(e => e.status === 'Completed' && e.completedAt)
    const durations = completed.map(e =>
      new Date(e.completedAt!).getTime() - new Date(e.startedAt).getTime()
    ).filter(d => d > 0)

    const avgResponse = durations.length > 0 ? durations.reduce((a, b) => a + b, 0) / durations.length : 0
    const p50 = percentile(durations, 0.5)
    const p95 = percentile(durations, 0.95)

    // Estimate "stuck" = running for > 5 min
    const now = Date.now()
    const stuck = running.filter(e => now - new Date(e.startedAt).getTime() > 5 * 60 * 1000)

    return {
      activeConversations: running.length,
      totalCompleted: completed.length,
      avgResponseMs: avgResponse,
      p50ResponseMs: p50,
      p95ResponseMs: p95,
      stuckCount: stuck.length,
      stuckExecs: stuck,
      durations,
    }
  }, [chatExecs])

  // Build histogram buckets for response time distribution
  const histogram = useMemo(() => {
    if (!metrics?.durations.length) return { values: [], labels: [] }
    const bucketSize = 5000 // 5s buckets
    const maxDuration = Math.max(...metrics.durations)
    const bucketCount = Math.min(Math.ceil(maxDuration / bucketSize), 20)
    const values = Array(bucketCount).fill(0)
    const labels: string[] = []
    for (let i = 0; i < bucketCount; i++) {
      labels.push(`${(i * bucketSize / 1000).toFixed(0)}s`)
    }
    for (const d of metrics.durations) {
      const idx = Math.min(Math.floor(d / bucketSize), bucketCount - 1)
      values[idx]++
    }
    return { values, labels }
  }, [metrics?.durations])

  const noChatWorkflows = chatWorkflows.length === 0

  return (
    <div className="p-4 space-y-5">
      {noChatWorkflows ? (
        <div className="flex flex-col items-center justify-center py-20 text-[#4A6B8A]">
          <div className="w-12 h-12 rounded-xl bg-[#0C1D38] flex items-center justify-center mb-3">
            <span className="text-xl text-[#3E5F7D]">?</span>
          </div>
          <p className="text-sm font-medium text-[#7596B8]">Nenhum workflow Chat configurado</p>
          <p className="text-xs text-[#3E5F7D] mt-1">Metricas de experiencia requerem workflows com inputMode=Chat</p>
        </div>
      ) : (
        <>
          {/* Summary cards */}
          <div className="grid grid-cols-4 gap-3">
            <MetricCard
              label="Active Conversations"
              value={metrics ? String(metrics.activeConversations) : '--'}
              color="blue"
              subtitle={metrics ? `${metrics.totalCompleted} concluidas` : undefined}
            />
            <MetricCard
              label="Avg Response"
              value={metrics ? fmtDuration(metrics.avgResponseMs) : '--'}
              color="violet"
            />
            <MetricCard
              label="P50 Response"
              value={metrics ? fmtDuration(metrics.p50ResponseMs) : '--'}
              color="emerald"
            />
            <MetricCard
              label="P95 Response"
              value={metrics ? fmtDuration(metrics.p95ResponseMs) : '--'}
              color={metrics && metrics.p95ResponseMs > 30_000 ? 'red' : 'amber'}
            />
          </div>

          {/* Response time distribution */}
          {histogram.values.length > 0 && (
            <div>
              <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">
                Distribuicao de Tempo de Resposta
              </div>
              <MiniBarChart
                label=""
                values={histogram.values}
                labels={histogram.labels}
                color="bg-[#0057E0]/60"
                height={56}
              />
            </div>
          )}

          {/* Stuck conversations */}
          <div>
            <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">
              Conversas Travadas ({metrics?.stuckCount ?? 0})
            </div>
            {metrics && metrics.stuckExecs.length > 0 ? (
              <div className="space-y-1.5">
                {metrics.stuckExecs.map(exec => {
                  const elapsed = ((Date.now() - new Date(exec.startedAt).getTime()) / 1000 / 60).toFixed(0)
                  return (
                    <div key={exec.executionId} className="flex items-center gap-3 px-3 py-2 rounded-lg bg-amber-500/5 border border-amber-500/10">
                      <span className="w-2 h-2 rounded-full bg-amber-400 animate-pulse shrink-0" />
                      <span className="text-[11px] text-[#B8CEE5] font-mono truncate flex-1">{exec.executionId.slice(0, 12)}</span>
                      <span className="text-[10px] text-amber-400 font-mono shrink-0">{elapsed}min</span>
                    </div>
                  )
                })}
              </div>
            ) : (
              <div className="text-center text-[#3E5F7D] text-xs py-4 rounded-lg border border-[#0C1D38] bg-[#081529]/20">
                Nenhuma conversa travada detectada
              </div>
            )}
          </div>

          {/* Note about limitations */}
          <div className="rounded-lg border border-[#0C1D38] bg-[#081529]/20 px-4 py-3">
            <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-1">Nota</div>
            <p className="text-[11px] text-[#4A6B8A] leading-relaxed">
              Metricas de UX sao aproximadas a partir de execucoes de workflows Chat.
              Para metricas completas (abandono, msgs/resolucao, conversas por usuario),
              e necessario o endpoint <code className="text-[#4D8EF5]/60">GET /api/conversations</code>.
            </p>
          </div>
        </>
      )}
    </div>
  )
}
