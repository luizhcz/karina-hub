import { useState, useEffect, useRef, useCallback } from 'react'
import { api } from '../api'
import { TimeRangeSelector, getFromDate } from './monitor/shared/TimeRangeSelector'
import type { WorkflowExecution, WorkflowDef, TimeRange } from '../types'

type Status = WorkflowExecution['status']

const STATUS_CFG: Record<Status, { color: string; bg: string; dot: string }> = {
  Pending:   { color: 'text-amber-400',   bg: 'bg-amber-400/10',   dot: 'bg-amber-400'   },
  Running:   { color: 'text-blue-400',    bg: 'bg-blue-400/10',    dot: 'bg-blue-400'    },
  Completed: { color: 'text-emerald-400', bg: 'bg-emerald-400/10', dot: 'bg-emerald-400' },
  Failed:    { color: 'text-red-400',     bg: 'bg-red-400/10',     dot: 'bg-red-400'     },
  Cancelled: { color: 'text-[#7596B8]',   bg: 'bg-[#0C1D38]',      dot: 'bg-[#7596B8]'      },
  Paused:    { color: 'text-[#4D8EF5]',  bg: 'bg-[#0057E0]/10',  dot: 'bg-[#0057E0]'  },
}

function timeAgo(date: string) {
  const s = Math.floor((Date.now() - new Date(date).getTime()) / 1000)
  if (s < 60) return `${s}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m`
  return `${Math.floor(m / 60)}h`
}

function duration(start: string, end?: string) {
  const ms = (end ? new Date(end) : new Date()).getTime() - new Date(start).getTime()
  const s = Math.floor(ms / 1000)
  if (s < 60) return `${s}s`
  return `${Math.floor(s / 60)}m${s % 60}s`
}

interface ExecRow { exec: WorkflowExecution; wfName: string }

export interface ExecStats {
  total: number
  running: number
  completed: number
  failed: number
  pending: number
  successRate: number
  avgDurationMs: number
  recentErrors: string[]
}

interface Props {
  workflows: WorkflowDef[]
  selectedExecId: string | null
  onSelect: (exec: WorkflowExecution) => void
  onStatsChange?: (stats: ExecStats) => void
}

export function ExecutionsMonitor({ workflows, selectedExecId, onSelect, onStatsChange }: Props) {
  const [rows, setRows] = useState<ExecRow[]>([])
  const [filter, setFilter] = useState<Status | 'all'>('all')
  const [workflowFilter, setWorkflowFilter] = useState('')
  const [timeRange, setTimeRange] = useState<TimeRange>('24h')
  const [tick, setTick] = useState(0)
  const [cancellingId, setCancellingId] = useState<string | null>(null)
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const tickRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const timeRangeRef = useRef(timeRange)
  timeRangeRef.current = timeRange

  const fetchAll = () => {
    const from = getFromDate(timeRangeRef.current)
    const wfMap = Object.fromEntries(workflows.map(w => [w.id, w.name]))
    api.getAllExecutions({ from, pageSize: 100 }).then(({ items }) => {
      const merged: ExecRow[] = items.map(exec => ({
        exec,
        wfName: wfMap[exec.workflowId] ?? exec.workflowId,
      }))
      merged.sort((a, b) => {
        const order: Record<string, number> = { Running: 0, Completed: 1, Failed: 2, Pending: 3, Paused: 4, Cancelled: 5 }
        const oa = order[a.exec.status] ?? 9
        const ob = order[b.exec.status] ?? 9
        if (oa !== ob) return oa - ob
        return new Date(b.exec.startedAt).getTime() - new Date(a.exec.startedAt).getTime()
      })
      setRows(merged)

      if (onStatsChange) {
        const completed = merged.filter(r => r.exec.status === 'Completed')
        const failed = merged.filter(r => r.exec.status === 'Failed')
        const finalized = completed.length + failed.length
        const durations = completed
          .filter(r => r.exec.completedAt)
          .map(r => new Date(r.exec.completedAt!).getTime() - new Date(r.exec.startedAt).getTime())
        const avgDurationMs = durations.length > 0 ? durations.reduce((a, b) => a + b, 0) / durations.length : 0
        const recentErrors = failed
          .slice(0, 3)
          .map(r => r.exec.errorMessage?.slice(0, 80) ?? 'Erro desconhecido')

        onStatsChange({
          total: merged.length,
          running: merged.filter(r => r.exec.status === 'Running').length,
          completed: completed.length,
          failed: failed.length,
          pending: merged.filter(r => r.exec.status === 'Pending').length,
          successRate: finalized > 0 ? (completed.length / finalized) * 100 : 0,
          avgDurationMs,
          recentErrors,
        })
      }
    })
  }

  useEffect(() => {
    fetchAll()
    if (timerRef.current) clearInterval(timerRef.current)
    timerRef.current = setInterval(fetchAll, 3000)
    return () => { if (timerRef.current) clearInterval(timerRef.current) }
  }, [timeRange]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    tickRef.current = setInterval(() => setTick(t => t + 1), 1000)
    return () => { if (tickRef.current) clearInterval(tickRef.current) }
  }, [])

  // suppress unused warning — tick forces re-render for live durations
  void tick

  const handleCancel = useCallback(async (e: React.MouseEvent, execId: string) => {
    e.stopPropagation()
    setCancellingId(execId)
    try {
      await api.cancelExecution(execId)
      fetchAll()
    } finally {
      setCancellingId(null)
    }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  const filtered = (workflowFilter ? rows.filter(r => r.exec.workflowId === workflowFilter) : rows)
    .filter(r => filter === 'all' || r.exec.status === filter)

  const exportCSV = useCallback(() => {
    const headers = ['ExecutionId', 'WorkflowId', 'WorkflowName', 'Status', 'StartedAt', 'CompletedAt', 'Duration(s)', 'ErrorMessage']
    const csvRows = filtered.map(({ exec, wfName }) => {
      const dur = exec.completedAt
        ? ((new Date(exec.completedAt).getTime() - new Date(exec.startedAt).getTime()) / 1000).toFixed(1)
        : ''
      return [
        exec.executionId,
        exec.workflowId,
        `"${wfName}"`,
        exec.status,
        exec.startedAt,
        exec.completedAt ?? '',
        dur,
        `"${(exec.errorMessage ?? '').replace(/"/g, "'").replace(/\n/g, ' ').slice(0, 200)}"`,
      ]
    })
    const csv = [headers, ...csvRows].map(r => r.join(',')).join('\n')
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `executions-${new Date().toISOString().slice(0, 10)}.csv`
    document.body.appendChild(a)
    a.click()
    document.body.removeChild(a)
    URL.revokeObjectURL(url)
  }, [filtered])

  const counts = rows.reduce((acc, r) => {
    acc[r.exec.status] = (acc[r.exec.status] ?? 0) + 1
    return acc
  }, {} as Record<string, number>)

  const hasRunning = (counts['Running'] ?? 0) > 0

  return (
    <div className="flex flex-col h-full">
      {/* Stats bar */}
      <div className="flex items-center gap-2 px-4 py-2.5 border-b border-[#0C1D38] shrink-0 flex-wrap">
        {workflows.length > 0 && (
          <select
            value={workflowFilter}
            onChange={e => setWorkflowFilter(e.target.value)}
            className="text-xs bg-[#0C1D38] border border-[#1A3357] text-[#B8CEE5] rounded-md px-2 py-1 focus:outline-none focus:border-[#0057E0]"
          >
            <option value="">Todos os Workflows</option>
            {workflows.map(wf => (
              <option key={wf.id} value={wf.id}>{wf.name}</option>
            ))}
          </select>
        )}
        <button
          onClick={() => setFilter('all')}
          className={`px-2.5 py-1 rounded-md text-xs font-medium transition-colors ${filter === 'all' ? 'bg-[#0C1D38] text-[#DCE8F5]' : 'text-[#4A6B8A] hover:text-[#B8CEE5]'}`}
        >
          Todas <span className="text-[#7596B8] ml-0.5">{rows.length}</span>
        </button>

        {(['Pending', 'Running', 'Completed', 'Failed'] as Status[]).map(s => {
          const cfg = STATUS_CFG[s]
          const count = counts[s] ?? 0
          return (
            <button
              key={s}
              onClick={() => setFilter(filter === s ? 'all' : s)}
              className={`flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs font-medium transition-colors ${
                filter === s ? `${cfg.bg} ${cfg.color}` : 'text-[#4A6B8A] hover:text-[#B8CEE5]'
              }`}
            >
              <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${cfg.dot} ${s === 'Running' ? 'animate-pulse' : ''}`} />
              {s} <span className="font-normal">{count}</span>
            </button>
          )
        })}

        <div className="ml-auto flex items-center gap-2">
          {hasRunning && (
            <span className="flex items-center gap-1 text-[10px] text-blue-400/60">
              <span className="w-1.5 h-1.5 rounded-full bg-blue-400 animate-pulse" />
              live
            </span>
          )}
          <TimeRangeSelector value={timeRange} onChange={setTimeRange} />
          <button
            onClick={exportCSV}
            title="Exportar CSV"
            className="flex items-center gap-1 px-2 py-1 rounded text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38] transition-colors"
          >
            ↓ CSV
          </button>
        </div>
      </div>

      {/* List */}
      <div className="flex-1 overflow-y-auto">
        {filtered.length === 0 ? (
          <div className="flex items-center justify-center h-full text-[#3E5F7D] text-sm">
            Nenhuma execução encontrada
          </div>
        ) : (
          <div className="divide-y divide-[#0C1D38]">
            {filtered.map(({ exec, wfName }) => {
              const cfg = STATUS_CFG[exec.status] ?? STATUS_CFG.Pending
              const isSelected = exec.executionId === selectedExecId
              const inputPreview = (exec.input ?? '').split(',')[0].trim().slice(0, 16) || exec.executionId.slice(0, 8)
              const isFailed = exec.status === 'Failed'
              const canCancel = exec.status === 'Running' || exec.status === 'Pending'
              const isCancelling = cancellingId === exec.executionId

              return (
                <div
                  key={exec.executionId}
                  onClick={() => onSelect(exec)}
                  className={`group w-full text-left px-4 py-2.5 cursor-pointer transition-colors hover:bg-[#081529] ${isSelected ? 'bg-[#081529] border-l-2 border-[#0057E0]' : 'border-l-2 border-transparent'}`}
                >
                  <div className="flex items-center gap-2">
                    <span className={`w-2 h-2 rounded-full shrink-0 ${cfg.dot} ${exec.status === 'Running' ? 'animate-pulse' : ''}`} />
                    <span className={`text-xs font-medium ${cfg.color}`}>{exec.status}</span>
                    <span className="text-[#4A6B8A] text-[11px] font-mono flex-1 truncate">{inputPreview}</span>
                    <span className="text-[10px] text-[#3E5F7D] shrink-0">{timeAgo(exec.startedAt)}</span>
                    {canCancel && (
                      <button
                        onClick={e => handleCancel(e, exec.executionId)}
                        disabled={isCancelling}
                        title="Cancelar execução"
                        className="opacity-0 group-hover:opacity-100 shrink-0 w-4 h-4 flex items-center justify-center rounded text-[#4A6B8A] hover:text-red-400 hover:bg-red-500/10 transition-all disabled:opacity-40"
                      >
                        {isCancelling
                          ? <span className="w-2.5 h-2.5 border border-red-400/40 border-t-red-400 rounded-full animate-spin block" />
                          : <span className="text-[10px] leading-none">✕</span>
                        }
                      </button>
                    )}
                  </div>

                  <div className="flex items-center gap-2 mt-0.5 pl-4">
                    <span className="text-[10px] text-[#3E5F7D] truncate flex-1">{wfName}</span>
                    <span className="text-[10px] text-[#3E5F7D] shrink-0 tabular-nums">
                      {duration(exec.startedAt, exec.completedAt)}
                    </span>
                  </div>

                  {isFailed && exec.errorMessage && (
                    <div className="pl-4 mt-1 rounded bg-red-500/5 border border-red-500/10 px-2 py-1">
                      <p className="text-[10px] text-red-400/80 font-mono leading-relaxed line-clamp-2">
                        {exec.errorMessage.slice(0, 160)}
                      </p>
                    </div>
                  )}
                  {!isFailed && exec.errorMessage && (
                    <p className="pl-4 mt-0.5 text-[10px] text-red-400/60 truncate">
                      {exec.errorMessage.slice(0, 80)}
                    </p>
                  )}
                </div>
              )
            })}

          </div>
        )}
      </div>
    </div>
  )
}
