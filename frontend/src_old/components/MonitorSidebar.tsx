import { useState, useEffect, useRef } from 'react'
import { tokenApi } from '../api'
import type { GlobalTokenSummary, ThroughputResult } from '../types'
import type { ExecStats } from './ExecutionsMonitor'

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
}

export function MonitorSidebar({ execStats }: Props) {
  const [summary, setSummary] = useState<GlobalTokenSummary | null>(null)
  const [throughput, setThroughput] = useState<ThroughputResult | null>(null)
  const [range, setRange] = useState<'1h' | '24h' | '7d' | '30d'>('24h')
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const getFrom = () => {
    const now = new Date()
    switch (range) {
      case '1h':  return new Date(now.getTime() - 60 * 60 * 1000).toISOString()
      case '24h': return new Date(now.getTime() - 24 * 60 * 60 * 1000).toISOString()
      case '7d':  return new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000).toISOString()
      case '30d': return new Date(now.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString()
    }
  }

  const fetchAll = () => {
    const from = getFrom()
    tokenApi.getSummary(from).then(setSummary).catch(console.error)
    tokenApi.getThroughput(from).then(setThroughput).catch(console.error)
  }

  useEffect(() => {
    fetchAll()
    timerRef.current = setInterval(fetchAll, 10_000)
    return () => { if (timerRef.current) clearInterval(timerRef.current) }
  }, [range])

  return (
    <div className="flex flex-col h-full overflow-y-auto">
      {/* ── Execution Overview ─────────────────────────────────── */}
      <div className="shrink-0">
        <div className="px-4 py-2.5 border-b border-[#0C1D38]">
          <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Executions</span>
        </div>

        {execStats ? (
          <div className="px-4 py-3 border-b border-[#0C1D38] space-y-3">
            <div className="grid grid-cols-4 gap-2">
              <StatusPill label="Running" value={execStats.running} color="blue" pulse={execStats.running > 0} />
              <StatusPill label="Pending" value={execStats.pending} color="amber" />
              <StatusPill label="Done" value={execStats.completed} color="emerald" />
              <StatusPill label="Failed" value={execStats.failed} color="red" />
            </div>

            <div className="flex items-center gap-4">
              <div className="flex-1 min-w-0">
                <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-0.5">Success Rate</div>
                <div className="flex items-center gap-2">
                  <div className="flex-1 h-1.5 bg-[#0C1D38] rounded-full overflow-hidden">
                    <div
                      className={`h-full rounded-full transition-all duration-500 ${
                        execStats.successRate >= 90 ? 'bg-emerald-500' :
                        execStats.successRate >= 70 ? 'bg-amber-500' : 'bg-red-500'
                      }`}
                      style={{ width: `${execStats.successRate}%` }}
                    />
                  </div>
                  <span className={`text-xs font-mono font-semibold shrink-0 ${
                    execStats.successRate >= 90 ? 'text-emerald-400' :
                    execStats.successRate >= 70 ? 'text-amber-400' : 'text-red-400'
                  }`}>
                    {execStats.successRate.toFixed(0)}%
                  </span>
                </div>
              </div>
              <div className="w-px h-8 bg-[#0C1D38]" />
              <div className="shrink-0">
                <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider mb-0.5">Avg Duration</div>
                <span className="text-xs font-mono font-semibold text-[#B8CEE5]">
                  {fmtDuration(execStats.avgDurationMs)}
                </span>
              </div>
            </div>

            {execStats.recentErrors.length > 0 && (
              <div>
                <div className="text-[10px] text-red-400/60 uppercase tracking-wider mb-1">Recent Errors</div>
                <div className="space-y-1">
                  {execStats.recentErrors.map((err, i) => (
                    <div key={i} className="text-[10px] text-red-400/80 bg-red-500/5 rounded px-2 py-1 truncate font-mono">
                      {err}
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ) : (
          <div className="px-4 py-6 border-b border-[#0C1D38] text-center text-[#3E5F7D] text-xs">
            Carregando...
          </div>
        )}
      </div>

      {/* ── Throughput Chart ───────────────────────────────────── */}
      <div className="shrink-0">
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-[#0C1D38]">
          <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Throughput</span>
          <div className="flex gap-1">
            {(['1h', '24h', '7d', '30d'] as const).map(r => (
              <button
                key={r}
                onClick={() => setRange(r)}
                className={`px-2 py-0.5 rounded text-[11px] font-medium transition-colors ${
                  range === r ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38]'
                }`}
              >
                {r}
              </button>
            ))}
          </div>
        </div>

        {throughput && throughput.buckets.length > 0 ? (
          <div className="px-4 py-3 border-b border-[#0C1D38] space-y-3">
            {/* Avg rates */}
            <div className="grid grid-cols-3 gap-2">
              <div className="text-center">
                <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">Exec/h</div>
                <span className="text-sm font-mono font-bold text-blue-400">{throughput.avgExecutionsPerHour.toFixed(1)}</span>
              </div>
              <div className="text-center">
                <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">Tokens/h</div>
                <span className="text-sm font-mono font-bold text-[#0057E0]">{fmtNum(Math.round(throughput.avgTokensPerHour))}</span>
              </div>
              <div className="text-center">
                <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">Calls/h</div>
                <span className="text-sm font-mono font-bold text-emerald-400">{throughput.avgCallsPerHour.toFixed(1)}</span>
              </div>
            </div>

            {/* Bar chart — executions */}
            <ThroughputChart
              label="Executions"
              buckets={throughput.buckets}
              getValue={b => b.executions}
              color="bg-blue-500/60"
              range={range}
            />

            {/* Bar chart — tokens */}
            <ThroughputChart
              label="Tokens"
              buckets={throughput.buckets}
              getValue={b => b.tokens}
              color="bg-[#0057E0]/60"
              range={range}
            />
          </div>
        ) : throughput ? (
          <div className="px-4 py-4 border-b border-[#0C1D38] text-center text-[#3E5F7D] text-xs">
            Sem dados de throughput no período
          </div>
        ) : (
          <div className="px-4 py-4 border-b border-[#0C1D38] text-center text-[#3E5F7D] text-xs">
            Carregando...
          </div>
        )}
      </div>

      {/* ── Token Usage ────────────────────────────────────────── */}
      <div className="shrink-0">
        <div className="px-4 py-2.5 border-b border-[#0C1D38]">
          <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Token Usage ({range})</span>
        </div>

        {summary ? (
          <>
            <div className="grid grid-cols-2 gap-2 px-4 py-3 border-b border-[#0C1D38]">
              <MetricCard label="Total" value={fmtNum(summary.totalTokens)} color="violet" />
              <MetricCard label="Chamadas" value={fmtNum(summary.totalCalls)} color="amber" />
              <MetricCard label="Input" value={fmtNum(summary.totalInput)} color="blue" />
              <MetricCard label="Output" value={fmtNum(summary.totalOutput)} color="emerald" />
            </div>

            <div className="flex items-center justify-between px-4 py-2 border-b border-[#0C1D38]">
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">Avg Latency</span>
              <span className="text-xs font-mono font-semibold text-[#B8CEE5]">{(summary.avgDurationMs / 1000).toFixed(1)}s</span>
            </div>

            {/* Per-Agent Breakdown */}
            <div className="px-4 py-3">
              {summary.byAgent.length === 0 ? (
                <div className="text-center text-[#3E5F7D] text-sm py-4">
                  Nenhum uso registrado
                </div>
              ) : (
                <div className="space-y-3">
                  {summary.byAgent.map(agent => {
                    const maxTokens = Math.max(...summary.byAgent.map(a => a.totalTokens), 1)
                    const pct = (agent.totalTokens / maxTokens) * 100
                    const inputPct = agent.totalTokens > 0 ? (agent.totalInput / agent.totalTokens) * 100 : 0

                    return (
                      <div key={agent.agentId} className="group">
                        <div className="flex items-baseline justify-between mb-1">
                          <span className="text-[11px] font-medium text-[#DCE8F5] truncate max-w-[180px]" title={agent.agentId}>
                            {agent.agentId}
                          </span>
                          <span className="text-[11px] font-mono text-[#7596B8] shrink-0 ml-2">{fmtNum(agent.totalTokens)}</span>
                        </div>

                        <div className="h-3.5 rounded-md overflow-hidden bg-[#0C1D38] relative">
                          <div
                            className="absolute inset-y-0 left-0 bg-blue-500/60 rounded-l-md"
                            style={{ width: `${pct * (inputPct / 100)}%` }}
                          />
                          <div
                            className="absolute inset-y-0 bg-emerald-500/60 rounded-r-md"
                            style={{
                              left: `${pct * (inputPct / 100)}%`,
                              width: `${pct * ((100 - inputPct) / 100)}%`
                            }}
                          />
                        </div>

                        <div className="flex items-center gap-2 mt-1 text-[10px] text-[#4A6B8A] flex-wrap">
                          <span className="flex items-center gap-1">
                            <span className="w-1.5 h-1.5 rounded-sm bg-blue-500/60" />
                            {fmtNum(agent.totalInput)}
                          </span>
                          <span className="flex items-center gap-1">
                            <span className="w-1.5 h-1.5 rounded-sm bg-emerald-500/60" />
                            {fmtNum(agent.totalOutput)}
                          </span>
                          <span className="text-[#254980]">|</span>
                          <span>{agent.callCount}x</span>
                          <span className="text-[#254980]">|</span>
                          <span>{(agent.avgDurationMs / 1000).toFixed(1)}s avg</span>
                        </div>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          </>
        ) : (
          <div className="px-4 py-8 text-center text-[#3E5F7D] text-sm">
            Carregando tokens...
          </div>
        )}
      </div>
    </div>
  )
}

// ── Throughput Bar Chart ──────────────────────────────────────────────────────

import type { ThroughputBucket } from '../types'

function ThroughputChart({ label, buckets, getValue, color, range }: {
  label: string
  buckets: ThroughputBucket[]
  getValue: (b: ThroughputBucket) => number
  color: string
  range: string
}) {
  const values = buckets.map(getValue)
  const max = Math.max(...values, 1)

  const fmtTime = (iso: string) => {
    const d = new Date(iso)
    if (range === '7d' || range === '30d') {
      return `${d.getDate().toString().padStart(2, '0')}/${(d.getMonth() + 1).toString().padStart(2, '0')}`
    }
    return `${d.getHours().toString().padStart(2, '0')}h`
  }

  // Limit bars displayed for readability
  const maxBars = range === '1h' ? 60 : range === '24h' ? 24 : range === '7d' ? 28 : 30
  const displayBuckets = buckets.slice(-maxBars)
  const displayValues = displayBuckets.map(getValue)

  return (
    <div>
      <div className="flex items-center justify-between mb-1.5">
        <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">{label}</span>
        <span className="text-[10px] text-[#3E5F7D] font-mono">max {fmtNum(max)}</span>
      </div>
      <div className="flex items-end gap-px h-10">
        {displayValues.map((v, i) => {
          const pct = max > 0 ? (v / max) * 100 : 0
          return (
            <div
              key={i}
              className="flex-1 min-w-0 group relative"
              title={`${fmtTime(displayBuckets[i].bucket)}: ${fmtNum(v)}`}
            >
              <div className="w-full bg-[#0C1D38] rounded-sm overflow-hidden" style={{ height: 40 }}>
                <div
                  className={`w-full ${color} rounded-sm transition-all duration-300`}
                  style={{ height: `${Math.max(pct, v > 0 ? 4 : 0)}%`, marginTop: `${100 - Math.max(pct, v > 0 ? 4 : 0)}%` }}
                />
              </div>
            </div>
          )
        })}
      </div>
      {/* Time axis labels */}
      <div className="flex justify-between mt-1">
        <span className="text-[9px] text-[#3E5F7D] font-mono">
          {displayBuckets.length > 0 ? fmtTime(displayBuckets[0].bucket) : ''}
        </span>
        <span className="text-[9px] text-[#3E5F7D] font-mono">
          {displayBuckets.length > 0 ? fmtTime(displayBuckets[displayBuckets.length - 1].bucket) : ''}
        </span>
      </div>
    </div>
  )
}

// ── Small components ─────────────────────────────────────────────────────────

function StatusPill({ label, value, color, pulse }: {
  label: string; value: number; color: 'blue' | 'amber' | 'emerald' | 'red'; pulse?: boolean
}) {
  const styles = {
    blue:    'text-blue-400',
    amber:   'text-amber-400',
    emerald: 'text-emerald-400',
    red:     'text-red-400',
  }
  const dotStyles = {
    blue:    'bg-blue-400',
    amber:   'bg-amber-400',
    emerald: 'bg-emerald-400',
    red:     'bg-red-400',
  }

  return (
    <div className="text-center">
      <div className="flex items-center justify-center gap-1 mb-0.5">
        <span className={`w-1.5 h-1.5 rounded-full ${dotStyles[color]} ${pulse ? 'animate-pulse' : ''}`} />
        <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider">{label}</span>
      </div>
      <span className={`text-sm font-mono font-bold ${styles[color]}`}>{value}</span>
    </div>
  )
}

function MetricCard({ label, value, color }: {
  label: string; value: string; color: 'violet' | 'blue' | 'emerald' | 'amber'
}) {
  const colors = {
    violet:  'bg-[#0057E0]/10 text-[#0057E0] border-[#0057E0]/20',
    blue:    'bg-blue-500/10 text-blue-400 border-blue-500/20',
    emerald: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
    amber:   'bg-amber-500/10 text-amber-400 border-amber-500/20',
  }

  return (
    <div className={`rounded-lg border ${colors[color]} px-2.5 py-2 overflow-hidden`}>
      <div className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5 truncate">{label}</div>
      <span className="text-base font-semibold font-mono truncate block">{value}</span>
    </div>
  )
}
