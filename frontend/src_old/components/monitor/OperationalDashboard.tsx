import { useMemo } from 'react'
import { MetricCard } from './shared/MetricCard'
import { StatusPill } from './shared/StatusPill'
import { MiniBarChart } from './shared/MiniBarChart'
import type { GlobalTokenSummary, ThroughputResult, TimeRange, ExecutionSummary, ExecutionTimeseries } from '../../types'
import type { WorkflowDef } from '../../types'
import type { ExecStats } from '../ExecutionsMonitor'

function fmtNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

function fmtDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`
  const s = ms / 1000
  if (s < 60) return `${s.toFixed(1)}s`
  return `${Math.floor(s / 60)}m${Math.round(s % 60)}s`
}

interface Props {
  execStats: ExecStats | null
  tokenSummary: GlobalTokenSummary | null
  throughput: ThroughputResult | null
  timeRange: TimeRange
  analyticsSummary?: ExecutionSummary | null
  analyticsTimeseries?: ExecutionTimeseries | null
  workflows?: WorkflowDef[]
  workflowSummaries?: Record<string, ExecutionSummary>
}

function WorkflowHealthTable({
  workflows,
  workflowSummaries,
}: {
  workflows: WorkflowDef[]
  workflowSummaries: Record<string, ExecutionSummary>
}) {
  if (workflows.length === 0) return null

  return (
    <div>
      <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Saúde por Workflow</div>
      <div className="bg-[#04091A] border border-[#0C1D38] rounded-xl overflow-hidden">
        {/* Header */}
        <div className="grid grid-cols-6 px-4 py-2 border-b border-[#0C1D38]">
          <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-2">Workflow</span>
          <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">Modo</span>
          <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right">Execuções</span>
          <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right">Taxa Sucesso</span>
          <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right">Status</span>
        </div>
        {/* Rows */}
        {workflows.map(wf => {
          const summary = workflowSummaries[wf.id]
          const successRate = summary?.successRate ?? null
          const successColor =
            successRate === null ? ''
            : successRate >= 90 ? 'text-emerald-400'
            : successRate >= 70 ? 'text-amber-400'
            : 'text-red-400'
          const dotColor =
            successRate === null ? 'bg-[#3E5F7D]'
            : successRate >= 90 ? 'bg-emerald-400'
            : successRate >= 70 ? 'bg-amber-400'
            : 'bg-red-400'
          const checkIcon =
            successRate === null ? ''
            : successRate >= 90 ? ' ✓'
            : ' ✗'

          return (
            <div
              key={wf.id}
              className="grid grid-cols-6 px-4 py-2.5 border-b border-[#0C1D38]/50 hover:bg-[#081529] transition-colors last:border-b-0"
            >
              <span className="text-xs text-[#DCE8F5] font-medium truncate col-span-2 pr-2">{wf.name}</span>
              <span className="text-[11px] text-[#7596B8] font-mono truncate">{wf.orchestrationMode}</span>
              <span className="text-[11px] text-[#B8CEE5] font-mono text-right">
                {summary ? summary.total : (
                  <span className="inline-block w-3 h-3 border border-[#4A6B8A]/40 border-t-[#4A6B8A] rounded-full animate-spin" />
                )}
              </span>
              <span className={`text-[11px] font-mono text-right ${successColor}`}>
                {summary ? (
                  <>{summary.successRate.toFixed(0)}%{checkIcon}</>
                ) : (
                  '--'
                )}
              </span>
              <div className="flex justify-end items-center">
                <span className={`w-2.5 h-2.5 rounded-full ${dotColor}`} />
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}

export function OperationalDashboard({ execStats, tokenSummary, throughput, timeRange, analyticsSummary, analyticsTimeseries, workflows, workflowSummaries }: Props) {
  const errorCategories = useMemo(() => {
    if (!execStats?.recentErrors.length) return []
    const map: Record<string, number> = {}
    for (const err of execStats.recentErrors) {
      const category = err.split(':')[0].split(' ').slice(0, 3).join(' ').trim() || 'Unknown'
      map[category] = (map[category] ?? 0) + 1
    }
    return Object.entries(map).sort((a, b) => b[1] - a[1])
  }, [execStats?.recentErrors])

  const bucketLabels = useMemo(() => {
    if (!throughput?.buckets.length) return []
    return throughput.buckets.map(b => {
      const d = new Date(b.bucket)
      if (timeRange === '7d' || timeRange === '30d')
        return `${d.getDate().toString().padStart(2, '0')}/${(d.getMonth() + 1).toString().padStart(2, '0')}`
      return `${d.getHours().toString().padStart(2, '0')}h`
    })
  }, [throughput?.buckets, timeRange])

  const timeseriesLabels = useMemo(() => {
    if (!analyticsTimeseries?.buckets.length) return []
    return analyticsTimeseries.buckets.map(b => {
      const d = new Date(b.bucket)
      if (timeRange === '7d' || timeRange === '30d')
        return `${d.getDate().toString().padStart(2, '0')}/${(d.getMonth() + 1).toString().padStart(2, '0')}`
      return `${d.getHours().toString().padStart(2, '0')}h`
    })
  }, [analyticsTimeseries?.buckets, timeRange])

  return (
    <div className="p-4 space-y-5">
      {/* Status pills */}
      {execStats && (
        <div className="grid grid-cols-4 gap-3">
          <StatusPill label="Running" value={execStats.running} color="blue" pulse={execStats.running > 0} />
          <StatusPill label="Pending" value={execStats.pending} color="amber" />
          <StatusPill label="Completed" value={execStats.completed} color="emerald" />
          <StatusPill label="Failed" value={execStats.failed} color="red" />
        </div>
      )}

      {/* Metric cards — row 1 */}
      <div className="grid grid-cols-4 gap-3">
        <MetricCard
          label="Success Rate"
          value={analyticsSummary ? `${analyticsSummary.successRate.toFixed(0)}%` : execStats ? `${execStats.successRate.toFixed(0)}%` : '--'}
          color={(analyticsSummary?.successRate ?? execStats?.successRate ?? 100) >= 90 ? 'emerald' : (analyticsSummary?.successRate ?? execStats?.successRate ?? 100) >= 70 ? 'amber' : 'red'}
          subtitle={analyticsSummary ? `${analyticsSummary.completed + analyticsSummary.failed} finalizadas` : execStats ? `${execStats.completed + execStats.failed} finalizadas` : undefined}
        />
        <MetricCard
          label="Avg Latency"
          value={analyticsSummary ? fmtDuration(analyticsSummary.avgDurationMs) : execStats ? fmtDuration(execStats.avgDurationMs) : '--'}
          color="blue"
        />
        <MetricCard
          label="Total Tokens"
          value={tokenSummary ? fmtNum(tokenSummary.totalTokens) : '--'}
          color="violet"
          subtitle={tokenSummary ? `${fmtNum(tokenSummary.totalCalls)} chamadas` : undefined}
        />
        <MetricCard
          label="Avg LLM Latency"
          value={tokenSummary ? `${(tokenSummary.avgDurationMs / 1000).toFixed(1)}s` : '--'}
          color="amber"
        />
      </div>

      {/* Metric cards — row 2: P50/P95 only */}
      {analyticsSummary && (
        <div className="grid grid-cols-2 gap-3">
          <MetricCard
            label="P50 Latency"
            value={fmtDuration(analyticsSummary.p50Ms)}
            color="blue"
            subtitle="mediana"
          />
          <MetricCard
            label="P95 Latency"
            value={fmtDuration(analyticsSummary.p95Ms)}
            color={analyticsSummary.p95Ms > 30000 ? 'red' : analyticsSummary.p95Ms > 15000 ? 'amber' : 'blue'}
            subtitle="percentil 95"
          />
        </div>
      )}

      {/* Executions timeseries (analytics backend) */}
      {analyticsTimeseries && analyticsTimeseries.buckets.length > 0 && (
        <div className="grid grid-cols-2 gap-4">
          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Execuções</span>
              <span className="text-[11px] text-[#4A6B8A] font-mono">
                {analyticsTimeseries.buckets.reduce((s, b) => s + b.total, 0)} total
              </span>
            </div>
            <MiniBarChart
              label=""
              values={analyticsTimeseries.buckets.map(b => b.total)}
              labels={timeseriesLabels}
              color="bg-blue-500/60"
              height={48}
            />
          </div>
          <div>
            <div className="flex items-center justify-between mb-2">
              <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Falhas</span>
              <span className="text-[11px] text-[#4A6B8A] font-mono">
                {analyticsTimeseries.buckets.reduce((s, b) => s + b.failed, 0)} total
              </span>
            </div>
            <MiniBarChart
              label=""
              values={analyticsTimeseries.buckets.map(b => b.failed)}
              labels={timeseriesLabels}
              color="bg-red-500/60"
              height={48}
            />
          </div>
        </div>
      )}

      {/* Throughput — Tokens chart */}
      {throughput && throughput.buckets.length > 0 && (
        <div>
          <div className="flex items-center justify-between mb-2">
            <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Tokens</span>
            <span className="text-[11px] text-[#4A6B8A] font-mono">{fmtNum(Math.round(throughput.avgTokensPerHour))}/h avg</span>
          </div>
          <MiniBarChart
            label=""
            values={throughput.buckets.map(b => b.tokens)}
            labels={bucketLabels}
            color="bg-[#0057E0]/60"
            height={48}
          />
        </div>
      )}

      {/* Workflow Health Table */}
      {workflows && workflows.length > 0 && (
        <WorkflowHealthTable
          workflows={workflows}
          workflowSummaries={workflowSummaries ?? {}}
        />
      )}

      {/* Error categories */}
      {errorCategories.length > 0 && (
        <div>
          <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Erros Recentes por Categoria</div>
          <div className="space-y-1.5">
            {errorCategories.map(([cat, count]) => (
              <div key={cat} className="flex items-center gap-3 px-3 py-2 rounded-lg bg-red-500/5 border border-red-500/10">
                <span className="text-red-400 text-xs font-mono font-medium">{count}x</span>
                <span className="text-xs text-red-300/80 truncate">{cat}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Loading states */}
      {!execStats && !tokenSummary && (
        <div className="flex items-center justify-center py-16 text-[#3E5F7D] text-sm">
          Carregando dados operacionais...
        </div>
      )}
    </div>
  )
}
