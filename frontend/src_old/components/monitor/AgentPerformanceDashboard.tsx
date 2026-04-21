import { useCallback, useMemo } from 'react'
import { pricingApi } from '../../api'
import { usePolledData } from '../../hooks/usePolledData'
import { MetricCard } from './shared/MetricCard'
import { MiniBarChart } from './shared/MiniBarChart'
import { DataTable } from './shared/DataTable'
import type { Column } from './shared/DataTable'
import type { GlobalTokenSummary, TimeRange, ModelPricing, AgentTokenSummary } from '../../types'

function fmtNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

function fmtCost(n: number): string {
  if (n < 0.01) return `$${n.toFixed(4)}`
  if (n < 1) return `$${n.toFixed(3)}`
  return `$${n.toFixed(2)}`
}

interface AgentRow extends AgentTokenSummary {
  cost: number
  efficiency: number
}

interface Props {
  tokenSummary: GlobalTokenSummary | null
  timeRange: TimeRange
}

export function AgentPerformanceDashboard({ tokenSummary }: Props) {
  const { data: pricing } = usePolledData<ModelPricing[]>(
    useCallback(() => pricingApi.getAll(), []),
    300_000, // refresh pricing every 5 min
    []
  )

  const pricingMap = useMemo(() => {
    if (!pricing) return new Map<string, ModelPricing>()
    const m = new Map<string, ModelPricing>()
    for (const p of pricing) m.set(p.modelId, p)
    return m
  }, [pricing])

  const agents: AgentRow[] = useMemo(() => {
    if (!tokenSummary?.byAgent) return []
    return tokenSummary.byAgent.map(a => {
      const p = pricingMap.get(a.modelId)
      const cost = p
        ? a.totalInput * p.pricePerInputToken + a.totalOutput * p.pricePerOutputToken
        : 0
      const efficiency = a.totalInput > 0 ? a.totalOutput / a.totalInput : 0
      return { ...a, cost, efficiency }
    })
  }, [tokenSummary?.byAgent, pricingMap])

  const totalCost = agents.reduce((s, a) => s + a.cost, 0)
  const totalTokens = tokenSummary?.totalTokens ?? 0
  const totalCalls = tokenSummary?.totalCalls ?? 0
  const avgLatency = tokenSummary?.avgDurationMs ?? 0

  // Group by model
  const modelBreakdown = useMemo(() => {
    const map: Record<string, { calls: number; tokens: number; totalDur: number }> = {}
    for (const a of agents) {
      const m = map[a.modelId] ?? (map[a.modelId] = { calls: 0, tokens: 0, totalDur: 0 })
      m.calls += a.callCount
      m.tokens += a.totalTokens
      m.totalDur += a.avgDurationMs * a.callCount
    }
    return Object.entries(map).map(([modelId, v]) => ({
      modelId,
      calls: v.calls,
      tokens: v.tokens,
      avgLatency: v.calls > 0 ? v.totalDur / v.calls : 0,
    })).sort((a, b) => b.tokens - a.tokens)
  }, [agents])

  const columns: Column<AgentRow>[] = [
    {
      key: 'agentId', label: 'Agent', align: 'left',
      render: r => (
        <div>
          <div className="text-[#DCE8F5] font-medium truncate max-w-[160px]">{r.agentId}</div>
          <div className="text-[10px] text-[#3E5F7D] font-mono">{r.modelId}</div>
        </div>
      ),
      sortValue: r => r.agentId,
    },
    {
      key: 'tokens', label: 'Tokens', align: 'right',
      render: r => <span className="text-[#4D8EF5] font-mono">{fmtNum(r.totalTokens)}</span>,
      sortValue: r => r.totalTokens,
    },
    {
      key: 'input', label: 'Input', align: 'right',
      render: r => <span className="text-blue-400 font-mono">{fmtNum(r.totalInput)}</span>,
      sortValue: r => r.totalInput,
    },
    {
      key: 'output', label: 'Output', align: 'right',
      render: r => <span className="text-emerald-400 font-mono">{fmtNum(r.totalOutput)}</span>,
      sortValue: r => r.totalOutput,
    },
    {
      key: 'calls', label: 'Calls', align: 'right',
      render: r => <span className="text-[#B8CEE5] font-mono">{fmtNum(r.callCount)}</span>,
      sortValue: r => r.callCount,
    },
    {
      key: 'latency', label: 'Avg Lat', align: 'right',
      render: r => <span className="text-[#7596B8] font-mono">{(r.avgDurationMs / 1000).toFixed(1)}s</span>,
      sortValue: r => r.avgDurationMs,
    },
    {
      key: 'efficiency', label: 'Out/In', align: 'right',
      render: r => <span className="text-amber-400 font-mono">{r.efficiency.toFixed(2)}</span>,
      sortValue: r => r.efficiency,
    },
    {
      key: 'cost', label: 'Cost', align: 'right',
      render: r => <span className={`font-mono ${r.cost > 0 ? 'text-emerald-400' : 'text-[#3E5F7D]'}`}>{r.cost > 0 ? fmtCost(r.cost) : '--'}</span>,
      sortValue: r => r.cost,
    },
  ]

  return (
    <div className="p-4 space-y-5">
      {/* Summary cards */}
      <div className="grid grid-cols-4 gap-3">
        <MetricCard label="Total Cost" value={totalCost > 0 ? fmtCost(totalCost) : '--'} color="emerald" subtitle={pricing?.length ? `${pricing.length} modelos com preco` : 'Sem pricing configurado'} />
        <MetricCard label="Total Tokens" value={fmtNum(totalTokens)} color="violet" />
        <MetricCard label="LLM Calls" value={fmtNum(totalCalls)} color="blue" />
        <MetricCard label="Avg Latency" value={avgLatency > 0 ? `${(avgLatency / 1000).toFixed(1)}s` : '--'} color="amber" />
      </div>

      {/* Agent ranking table */}
      <div>
        <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Performance por Agente</div>
        <DataTable columns={columns} data={agents} keyFn={r => r.agentId} emptyMessage="Nenhum dado de agente no periodo" />
      </div>

      {/* Model breakdown */}
      {modelBreakdown.length > 0 && (
        <div className="grid grid-cols-2 gap-4">
          <div>
            <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Tokens por Modelo</div>
            <MiniBarChart
              label=""
              values={modelBreakdown.map(m => m.tokens)}
              labels={modelBreakdown.map(m => m.modelId.split('/').pop() ?? m.modelId)}
              color="bg-[#0057E0]/60"
              height={48}
            />
          </div>
          <div>
            <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Latencia por Modelo</div>
            <MiniBarChart
              label=""
              values={modelBreakdown.map(m => m.avgLatency)}
              labels={modelBreakdown.map(m => m.modelId.split('/').pop() ?? m.modelId)}
              color="bg-amber-500/60"
              height={48}
            />
          </div>
        </div>
      )}

      {!tokenSummary && (
        <div className="flex items-center justify-center py-16 text-[#3E5F7D] text-sm">
          Carregando dados de performance...
        </div>
      )}
    </div>
  )
}
