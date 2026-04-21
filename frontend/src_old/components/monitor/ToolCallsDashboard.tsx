import { useState, useEffect, useMemo, useCallback, useRef } from 'react'
import { api, toolsApi } from '../../api'
import { MetricCard } from './shared/MetricCard'
import { DataTable } from './shared/DataTable'
import type { Column } from './shared/DataTable'
import type { ToolInvocation, WorkflowDef, TimeRange } from '../../types'

function fmtNum(n: number): string {
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

function percentile(values: number[], p: number): number {
  if (values.length === 0) return 0
  const sorted = [...values].sort((a, b) => a - b)
  const idx = Math.ceil(sorted.length * p) - 1
  return sorted[Math.max(0, idx)]
}

interface ToolSummary {
  toolName: string
  totalCalls: number
  successCount: number
  failCount: number
  successRate: number
  avgDurationMs: number
  p95DurationMs: number
}

interface AgentToolEntry {
  key: string
  agentId: string
  toolName: string
  callCount: number
  successRate: number
}

interface Props {
  workflows: WorkflowDef[]
  timeRange: TimeRange
}

export function ToolCallsDashboard({ workflows }: Props) {
  const [invocations, setInvocations] = useState<ToolInvocation[]>([])
  const [loading, setLoading] = useState(true)
  const workflowsRef = useRef(workflows)
  workflowsRef.current = workflows

  // Fetch tool invocations for recent executions across all workflows
  const fetchTools = useCallback(async () => {
    const wfs = workflowsRef.current
    if (wfs.length === 0) return
    try {
      const allExecs = await Promise.all(
        wfs.map(wf => api.getWorkflowExecutions(wf.id, undefined, 10))
      )
      const execIds = allExecs.flat().map(e => e.executionId).slice(0, 20)
      const allTools = await Promise.all(execIds.map(id => toolsApi.getByExecution(id)))
      setInvocations(allTools.flat())
    } catch {
      setInvocations([])
    } finally {
      setLoading(false)
    }
  }, [])

  // Only re-init when workflow count changes (not on every array ref change)
  const wfCount = workflows.length
  useEffect(() => {
    fetchTools()
    const timer = setInterval(fetchTools, 30_000)
    return () => clearInterval(timer)
  }, [fetchTools, wfCount])

  const toolSummaries = useMemo((): ToolSummary[] => {
    const map: Record<string, ToolInvocation[]> = {}
    for (const inv of invocations) {
      ;(map[inv.toolName] ??= []).push(inv)
    }
    return Object.entries(map).map(([toolName, invs]) => {
      const successCount = invs.filter(i => i.success).length
      const durations = invs.map(i => i.durationMs)
      return {
        toolName,
        totalCalls: invs.length,
        successCount,
        failCount: invs.length - successCount,
        successRate: invs.length > 0 ? (successCount / invs.length) * 100 : 0,
        avgDurationMs: durations.length > 0 ? durations.reduce((a, b) => a + b, 0) / durations.length : 0,
        p95DurationMs: percentile(durations, 0.95),
      }
    }).sort((a, b) => b.totalCalls - a.totalCalls)
  }, [invocations])

  const agentToolMap = useMemo((): AgentToolEntry[] => {
    const map: Record<string, { total: number; success: number }> = {}
    for (const inv of invocations) {
      const key = `${inv.agentId}::${inv.toolName}`
      const entry = map[key] ?? (map[key] = { total: 0, success: 0 })
      entry.total++
      if (inv.success) entry.success++
    }
    return Object.entries(map).map(([key, v]) => {
      const [agentId, toolName] = key.split('::')
      return {
        key,
        agentId,
        toolName,
        callCount: v.total,
        successRate: v.total > 0 ? (v.success / v.total) * 100 : 0,
      }
    }).sort((a, b) => b.callCount - a.callCount)
  }, [invocations])

  const recentErrors = useMemo(() =>
    invocations.filter(i => !i.success).slice(0, 10),
    [invocations]
  )

  const totalCalls = invocations.length
  const overallSuccess = totalCalls > 0 ? (invocations.filter(i => i.success).length / totalCalls) * 100 : 0
  const avgDuration = totalCalls > 0 ? invocations.reduce((s, i) => s + i.durationMs, 0) / totalCalls : 0
  const distinctTools = toolSummaries.length

  const toolColumns: Column<ToolSummary>[] = [
    {
      key: 'toolName', label: 'Tool', align: 'left',
      render: r => <span className="text-[#DCE8F5] font-medium font-mono">{r.toolName}</span>,
      sortValue: r => r.toolName,
    },
    {
      key: 'calls', label: 'Calls', align: 'right',
      render: r => <span className="text-[#B8CEE5] font-mono">{r.totalCalls}</span>,
      sortValue: r => r.totalCalls,
    },
    {
      key: 'success', label: 'Success', align: 'right',
      render: r => (
        <span className={`font-mono ${r.successRate >= 90 ? 'text-emerald-400' : r.successRate >= 70 ? 'text-amber-400' : 'text-red-400'}`}>
          {r.successRate.toFixed(0)}%
        </span>
      ),
      sortValue: r => r.successRate,
    },
    {
      key: 'avgDur', label: 'Avg', align: 'right',
      render: r => <span className="text-[#7596B8] font-mono">{r.avgDurationMs < 1000 ? `${Math.round(r.avgDurationMs)}ms` : `${(r.avgDurationMs / 1000).toFixed(1)}s`}</span>,
      sortValue: r => r.avgDurationMs,
    },
    {
      key: 'p95Dur', label: 'P95', align: 'right',
      render: r => <span className="text-[#4A6B8A] font-mono">{r.p95DurationMs < 1000 ? `${Math.round(r.p95DurationMs)}ms` : `${(r.p95DurationMs / 1000).toFixed(1)}s`}</span>,
      sortValue: r => r.p95DurationMs,
    },
    {
      key: 'errors', label: 'Errors', align: 'right',
      render: r => <span className={`font-mono ${r.failCount > 0 ? 'text-red-400' : 'text-[#3E5F7D]'}`}>{r.failCount}</span>,
      sortValue: r => r.failCount,
    },
  ]

  return (
    <div className="p-4 space-y-5">
      {/* Summary cards */}
      <div className="grid grid-cols-4 gap-3">
        <MetricCard label="Total Calls" value={fmtNum(totalCalls)} color="violet" />
        <MetricCard label="Success Rate" value={totalCalls > 0 ? `${overallSuccess.toFixed(0)}%` : '--'} color={overallSuccess >= 90 ? 'emerald' : 'red'} />
        <MetricCard label="Avg Latency" value={avgDuration > 0 ? `${avgDuration < 1000 ? `${Math.round(avgDuration)}ms` : `${(avgDuration / 1000).toFixed(1)}s`}` : '--'} color="blue" />
        <MetricCard label="Distinct Tools" value={String(distinctTools)} color="amber" />
      </div>

      {/* Tool summary table */}
      <div>
        <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Tools</div>
        <DataTable columns={toolColumns} data={toolSummaries} keyFn={r => r.toolName} emptyMessage="Nenhuma tool call registrada" />
      </div>

      {/* Agent-Tool heatmap + Error list */}
      <div className="grid grid-cols-2 gap-4">
        {/* Heatmap */}
        <div>
          <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Mapa Agente x Tool</div>
          {agentToolMap.length > 0 ? (
            <div className="space-y-1">
              {agentToolMap.slice(0, 15).map(entry => {
                const maxCalls = agentToolMap[0]?.callCount ?? 1
                const opacity = Math.max(0.2, entry.callCount / maxCalls)
                return (
                  <div key={entry.key} className="flex items-center gap-2 px-2 py-1.5 rounded-md bg-[#0C1D38]">
                    <span className="text-[11px] text-[#B8CEE5] truncate w-24 shrink-0 font-mono">{entry.agentId}</span>
                    <span className="text-[10px] text-[#4A6B8A]">&rarr;</span>
                    <span className="text-[11px] text-[#7596B8] truncate w-28 shrink-0 font-mono">{entry.toolName}</span>
                    <div className="flex-1 h-3 bg-[#0C1D38] rounded-sm overflow-hidden">
                      <div
                        className="h-full bg-[#0057E0] rounded-sm"
                        style={{ width: `${(entry.callCount / maxCalls) * 100}%`, opacity }}
                      />
                    </div>
                    <span className="text-[10px] text-[#4A6B8A] font-mono shrink-0 w-8 text-right">{entry.callCount}x</span>
                    <span className={`text-[10px] font-mono shrink-0 w-10 text-right ${entry.successRate >= 90 ? 'text-emerald-400' : 'text-red-400'}`}>
                      {entry.successRate.toFixed(0)}%
                    </span>
                  </div>
                )
              })}
            </div>
          ) : (
            <div className="text-center text-[#3E5F7D] text-xs py-6">Sem dados</div>
          )}
        </div>

        {/* Recent errors */}
        <div>
          <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Erros Recentes</div>
          {recentErrors.length > 0 ? (
            <div className="space-y-1.5">
              {recentErrors.map((inv, i) => (
                <div key={i} className="px-3 py-2 rounded-lg bg-red-500/5 border border-red-500/10">
                  <div className="flex items-baseline gap-2 mb-0.5">
                    <span className="text-[11px] text-red-400 font-medium font-mono">{inv.toolName}</span>
                    <span className="text-[10px] text-[#3E5F7D] font-mono">{inv.agentId}</span>
                  </div>
                  <p className="text-[10px] text-red-400/80 font-mono truncate">{inv.errorMessage ?? 'Erro desconhecido'}</p>
                </div>
              ))}
            </div>
          ) : (
            <div className="text-center text-[#3E5F7D] text-xs py-6">Nenhum erro recente</div>
          )}
        </div>
      </div>

      {loading && invocations.length === 0 && (
        <div className="flex items-center justify-center py-16 text-[#3E5F7D] text-sm">
          Carregando tool calls...
        </div>
      )}
    </div>
  )
}
