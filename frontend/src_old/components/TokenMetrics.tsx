import { useState, useEffect, useRef } from 'react'
import { tokenApi } from '../api'
import type { GlobalTokenSummary } from '../types'

function fmtNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

export function TokenMetrics() {
  const [summary, setSummary] = useState<GlobalTokenSummary | null>(null)
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

  const fetchSummary = () => {
    tokenApi.getSummary(getFrom()).then(setSummary).catch(console.error)
  }

  useEffect(() => {
    fetchSummary()
    timerRef.current = setInterval(fetchSummary, 10_000)
    return () => { if (timerRef.current) clearInterval(timerRef.current) }
  }, [range])

  if (!summary) return null

  const maxTokens = Math.max(...(summary.byAgent.map(a => a.totalTokens)), 1)

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* Header + Range Selector */}
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-[#0C1D38] shrink-0">
        <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Token Usage</span>
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

      {/* Summary Cards */}
      <div className="grid grid-cols-4 gap-3 px-4 py-3 border-b border-[#0C1D38] shrink-0">
        <MetricCard label="Total Tokens" value={fmtNum(summary.totalTokens)} icon="T" color="violet" />
        <MetricCard label="Input" value={fmtNum(summary.totalInput)} icon="I" color="blue" />
        <MetricCard label="Output" value={fmtNum(summary.totalOutput)} icon="O" color="emerald" />
        <MetricCard label="Chamadas LLM" value={fmtNum(summary.totalCalls)} icon="#" color="amber" />
      </div>

      {/* Per-Agent Breakdown */}
      <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
        {summary.byAgent.length === 0 ? (
          <div className="flex items-center justify-center h-full text-[#3E5F7D] text-sm">
            Nenhum uso de tokens registrado
          </div>
        ) : (
          <div className="space-y-3">
            {summary.byAgent.map(agent => {
              const pct = (agent.totalTokens / maxTokens) * 100
              const inputPct = agent.totalTokens > 0 ? (agent.totalInput / agent.totalTokens) * 100 : 0

              return (
                <div key={agent.agentId} className="group">
                  <div className="flex items-baseline justify-between mb-1.5">
                    <div className="flex items-center gap-2 min-w-0">
                      <span className="text-xs font-medium text-[#DCE8F5] truncate">{agent.agentId}</span>
                      <span className="text-[10px] text-[#3E5F7D] font-mono shrink-0">{agent.modelId}</span>
                    </div>
                    <span className="text-xs font-mono text-[#7596B8] shrink-0 ml-2">{fmtNum(agent.totalTokens)}</span>
                  </div>

                  {/* Token bar with input/output split */}
                  <div className="h-4 rounded-md overflow-hidden bg-[#0C1D38] relative">
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

                  {/* Stats row */}
                  <div className="flex items-center gap-3 mt-1.5 text-[10px] text-[#4A6B8A]">
                    <span className="flex items-center gap-1">
                      <span className="w-2 h-2 rounded-sm bg-blue-500/60" />
                      Input: {fmtNum(agent.totalInput)}
                    </span>
                    <span className="flex items-center gap-1">
                      <span className="w-2 h-2 rounded-sm bg-emerald-500/60" />
                      Output: {fmtNum(agent.totalOutput)}
                    </span>
                    <span className="text-[#3E5F7D]">|</span>
                    <span>{agent.callCount} chamadas</span>
                    <span className="text-[#3E5F7D]">|</span>
                    <span>avg {(agent.avgDurationMs / 1000).toFixed(1)}s</span>
                  </div>
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}

function MetricCard({ label, value, icon, color }: {
  label: string; value: string; icon: string; color: 'violet' | 'blue' | 'emerald' | 'amber'
}) {
  const colors = {
    violet:  'bg-[#0057E0]/10 text-[#0057E0] border-[#0057E0]/20',
    blue:    'bg-blue-500/10 text-blue-400 border-blue-500/20',
    emerald: 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20',
    amber:   'bg-amber-500/10 text-amber-400 border-amber-500/20',
  }

  return (
    <div className={`rounded-lg border ${colors[color]} px-3 py-2`}>
      <div className="flex items-center gap-1.5 mb-1">
        <span className="text-[10px] font-bold opacity-60">{icon}</span>
        <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium">{label}</span>
      </div>
      <span className="text-lg font-semibold font-mono">{value}</span>
    </div>
  )
}
