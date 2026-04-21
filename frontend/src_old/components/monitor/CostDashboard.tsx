import { useState, useEffect } from 'react'
import { tokenApi, pricingApi } from '../../api'
import { MetricCard } from './shared/MetricCard'
import { MiniBarChart } from './shared/MiniBarChart'
import { getFromDate } from './shared/TimeRangeSelector'
import type { TimeRange, ModelPricing } from '../../types'

interface AgentCost {
  agentId: string
  modelId: string
  inputTokens: number
  outputTokens: number
  costUSD: number
}

function fmtUSD(n: number): string {
  if (n < 0.001) return `$${n.toFixed(6)}`
  if (n < 0.1) return `$${n.toFixed(4)}`
  if (n < 10) return `$${n.toFixed(3)}`
  return `$${n.toFixed(2)}`
}

function fmtNum(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M'
  if (n >= 1_000) return (n / 1_000).toFixed(1) + 'K'
  return n.toLocaleString()
}

interface Props {
  timeRange: TimeRange
}

export function CostDashboard({ timeRange }: Props) {
  const [agentCosts, setAgentCosts] = useState<AgentCost[]>([])
  const [totalCost, setTotalCost] = useState(0)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    setLoading(true)
    const from = getFromDate(timeRange)
    Promise.all([
      tokenApi.getSummary(from),
      pricingApi.getAll(),
    ]).then(([summary, pricings]) => {
      const pricingMap: Record<string, ModelPricing> = {}
      for (const p of pricings) {
        pricingMap[p.modelId.toLowerCase()] = p
      }

      const costs: AgentCost[] = summary.byAgent.map(agent => {
        const pricing = pricingMap[agent.modelId.toLowerCase()]
        const costUSD = pricing
          ? (agent.totalInput * pricing.pricePerInputToken) + (agent.totalOutput * pricing.pricePerOutputToken)
          : 0
        return {
          agentId: agent.agentId,
          modelId: agent.modelId,
          inputTokens: agent.totalInput,
          outputTokens: agent.totalOutput,
          costUSD,
        }
      }).sort((a, b) => b.costUSD - a.costUSD)

      const total = costs.reduce((s, c) => s + c.costUSD, 0)
      setAgentCosts(costs)
      setTotalCost(total)
    }).catch(() => {
      setAgentCosts([])
      setTotalCost(0)
    }).finally(() => setLoading(false))
  }, [timeRange])

  const top5 = agentCosts.slice(0, 5)
  const chartLabels = top5.map(c => c.agentId.slice(0, 8))
  const chartValues = top5.map(c => c.costUSD)

  if (loading) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="w-4 h-4 border-[1.5px] border-[#254980] border-t-[#7596B8] rounded-full animate-spin" />
      </div>
    )
  }

  return (
    <div className="p-4 space-y-5">
      {/* Summary cards */}
      <div className="grid grid-cols-3 gap-3">
        <MetricCard
          label="Custo Total"
          value={fmtUSD(totalCost)}
          color="violet"
          subtitle={`período: ${timeRange}`}
        />
        <MetricCard
          label="Agentes com Custo"
          value={String(agentCosts.filter(c => c.costUSD > 0).length)}
          color="blue"
          subtitle={`de ${agentCosts.length} total`}
        />
        <MetricCard
          label="Maior Gasto"
          value={agentCosts.length > 0 ? fmtUSD(agentCosts[0].costUSD) : '--'}
          color="amber"
          subtitle={agentCosts.length > 0 ? agentCosts[0].agentId.slice(0, 16) : undefined}
        />
      </div>

      {/* Top 5 chart */}
      {top5.length > 0 && (
        <div>
          <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Top 5 Agentes por Custo</div>
          <MiniBarChart
            label=""
            values={chartValues}
            labels={chartLabels}
            color="bg-violet-500/60"
            height={56}
          />
        </div>
      )}

      {/* Cost table */}
      {agentCosts.length === 0 ? (
        <div className="text-center text-[#3E5F7D] text-sm py-8">
          Sem dados de custo — verifique se os preços de modelo estão configurados em Admin → Model Pricing
        </div>
      ) : (
        <div>
          <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider mb-2">Detalhamento por Agente</div>
          <div className="bg-[#04091A] border border-[#0C1D38] rounded-xl overflow-hidden">
            {/* Header */}
            <div className="grid grid-cols-12 px-4 py-2 border-b border-[#0C1D38]">
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-4">Agente</span>
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider col-span-3">Modelo</span>
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-2">Tokens Input</span>
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-2">Tokens Output</span>
              <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider text-right col-span-1">Custo</span>
            </div>
            {/* Rows */}
            {agentCosts.map((c, i) => {
              const pct = totalCost > 0 ? (c.costUSD / totalCost) * 100 : 0
              return (
                <div
                  key={c.agentId}
                  className="grid grid-cols-12 px-4 py-2.5 border-b border-[#0C1D38]/50 hover:bg-[#081529] transition-colors last:border-b-0"
                >
                  <div className="col-span-4 flex items-center gap-2 min-w-0 pr-2">
                    {i < 3 && (
                      <span className="text-[10px] text-[#4A6B8A] w-3 shrink-0">#{i + 1}</span>
                    )}
                    <span className="text-xs text-[#DCE8F5] font-medium truncate">{c.agentId}</span>
                  </div>
                  <span className="text-[11px] text-[#7596B8] font-mono col-span-3 truncate pr-2">{c.modelId}</span>
                  <span className="text-[11px] text-[#B8CEE5] font-mono text-right col-span-2">{fmtNum(c.inputTokens)}</span>
                  <span className="text-[11px] text-[#B8CEE5] font-mono text-right col-span-2">{fmtNum(c.outputTokens)}</span>
                  <div className="col-span-1 flex flex-col items-end">
                    <span className={`text-[11px] font-mono font-semibold ${c.costUSD > 0 ? 'text-violet-400' : 'text-[#3E5F7D]'}`}>
                      {c.costUSD > 0 ? fmtUSD(c.costUSD) : '--'}
                    </span>
                    {pct > 0 && (
                      <span className="text-[9px] text-[#4A6B8A]">{pct.toFixed(0)}%</span>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
          <p className="text-[10px] text-[#3E5F7D] mt-2">
            * Custo calculado com base nos preços configurados em Admin → Model Pricing. Agentes sem pricing configurado mostram "--".
          </p>
        </div>
      )}
    </div>
  )
}
