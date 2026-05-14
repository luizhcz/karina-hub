import { useEffect, useMemo, useState } from 'react'
import {
  getProjectAgents,
  getProjectBudget,
  getProjectOverview,
  getProjectTimeseries,
  type ProjectAgentBreakdown,
  type ProjectBudgetStatus,
  type ProjectOverview,
  type ProjectTimeseriesBucket,
} from '../api/projectAnalytics'
import { ApiError, friendlyError } from '../api/client'
import { getIdentity } from '../stores/identity'
import {
  Badge,
  Button,
  Card,
  CardHeader,
  ErrorMessage,
  Spinner,
  cn,
} from '../ui'

type Granularity = 'day' | 'hour'
type ChartType = 'line' | 'bar'

interface DashboardData {
  overview: ProjectOverview
  timeseries: ProjectTimeseriesBucket[]
  agents: ProjectAgentBreakdown[]
  budget: ProjectBudgetStatus
}

export function Dashboard() {
  const identity = useMemo(() => getIdentity(), [])
  const projectId = identity?.projectId ?? null

  const [granularity, setGranularity] = useState<Granularity>('day')
  const [chartType, setChartType] = useState<ChartType>('line')
  const [data, setData] = useState<DashboardData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [forbidden, setForbidden] = useState(false)

  const reload = async (signalGranularity: Granularity) => {
    if (!projectId) return
    setLoading(true)
    setError(null)
    setForbidden(false)
    try {
      // Backend já filtra por agentes do próprio ProjectId via ownedOnly=true —
      // métricas LLM (cost/tokens/calls) e topAgents do overview, agentes da
      // tabela e buckets do timeseries. Métricas de execução continuam
      // project-wide porque granularidade é por workflow, não por agent.
      const [overview, timeseries, agents, budget] = await Promise.all([
        getProjectOverview(projectId, undefined, undefined, true),
        getProjectTimeseries(projectId, signalGranularity, undefined, undefined, undefined, true),
        getProjectAgents(projectId, 20, undefined, undefined, true),
        getProjectBudget(projectId),
      ])
      setData({ overview, timeseries, agents, budget })
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setForbidden(true)
        setData(null)
      } else {
        setError(friendlyError(err, 'Não foi possível carregar o dashboard.'))
      }
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void reload(granularity)
  }, [projectId, granularity])

  if (!projectId) {
    return (
      <Card padded className="mx-auto max-w-3xl text-center">
        <p className="text-sm text-fg-muted">
          Selecione um projeto pra abrir o dashboard de uso.
        </p>
      </Card>
    )
  }

  return (
    <div className="mx-auto max-w-6xl">
      <div className="mb-8 flex items-end justify-between gap-4">
        <div>
          <h1 className="text-[28px] font-semibold tracking-tight">Dashboard</h1>
          <p className="mt-2 text-sm text-fg-muted">
            Uso e custo do seu projeto no mês corrente. Dados defasados em até 30 minutos
            (refresh do agregador de custos).
          </p>
        </div>
        <Button variant="secondary" size="sm" onClick={() => reload(granularity)}>
          Atualizar
        </Button>
      </div>

      {loading && !data && (
        <Card className="flex items-center justify-center py-12">
          <Spinner className="h-6 w-6 text-fg-muted" />
        </Card>
      )}

      {forbidden && (
        <ErrorMessage
          message="Você não tem permissão pra ver analytics de outro projeto. Confirme com o time de governança se precisa de acesso adicional."
        />
      )}

      {error && <ErrorMessage message={error} />}

      {data && (
        <div className="space-y-6">
          <OverviewCards overview={data.overview} />

          <Card className="space-y-4">
            <div className="flex items-end justify-between gap-3">
              <CardHeader
                title="Custo e execuções no período"
                description={
                  chartType === 'line'
                    ? 'Cada ponto representa o intervalo escolhido (dia ou hora).'
                    : 'Cada barra representa o intervalo escolhido (dia ou hora).'
                }
              />
              <div className="flex shrink-0 items-center gap-2">
                <ChartTypeToggle value={chartType} onChange={setChartType} />
                <GranularityToggle value={granularity} onChange={setGranularity} />
              </div>
            </div>
            <Spark buckets={data.timeseries} mode={chartType} />
            <p className="text-[11px] leading-relaxed text-fg-dim">
              Custo, tokens e LLM calls são só dos agentes do projeto. Execuções/falhas
              continuam project-wide (granularidade por workflow).
            </p>
          </Card>

          <Card className="space-y-4">
            <CardHeader
              title="Agentes do projeto"
              description="Top 20 por custo no período. p95 reflete a duração das chamadas LLM no agente; error rate é por execução do workflow envolvendo o agente."
            />
            <AgentsTable rows={data.agents} />
          </Card>

          <BudgetCard status={data.budget} />
        </div>
      )}
    </div>
  )
}

interface OverviewCardsProps {
  overview: ProjectOverview
}

function OverviewCards({ overview }: OverviewCardsProps) {
  const successPct = overview.completed + overview.failed > 0 ? overview.successRate : null

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <KpiCard
        label="Custo MTD"
        value={formatUsd(overview.totalCostUsd)}
        secondary={`${formatNumber(overview.totalCalls)} chamada${overview.totalCalls === 1 ? '' : 's'} de LLM`}
      />
      <KpiCard
        label="Tokens MTD"
        value={formatNumber(overview.totalTokens)}
        secondary="entrada + saída"
      />
      <KpiCard
        label="Execuções"
        value={formatNumber(overview.totalExecutions)}
        secondary={`${overview.completed} ok · ${overview.failed} falha${overview.failed === 1 ? '' : 's'}`}
      />
      <KpiCard
        label="Taxa de sucesso"
        value={successPct === null ? '—' : `${(successPct * 100).toFixed(1)}%`}
        secondary={
          successPct === null
            ? 'sem execuções no período'
            : successPct >= 0.95
              ? 'dentro do esperado'
              : 'abaixo do esperado'
        }
      />

      {overview.topAgents.length > 0 && (
        <Card padded className="sm:col-span-2 lg:col-span-4">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">
            Top agentes por custo
          </p>
          <ul className="mt-2 grid grid-cols-1 gap-2 sm:grid-cols-3">
            {overview.topAgents.map((a, idx) => (
              <li
                key={a.agentId}
                className="flex items-center justify-between rounded-md border border-border bg-bg-soft px-3 py-2 text-xs"
                title={a.agentId}
              >
                <div className="flex min-w-0 items-center gap-2">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent-subtle text-[10px] font-bold text-accent">
                    {idx + 1}
                  </span>
                  {/* Nome quando disponível; fallback pro id (font-mono pra denotar) em
                      agentes deletados que ainda têm consumo histórico no período. */}
                  <span className={a.agentName ? 'truncate' : 'truncate font-mono'}>
                    {a.agentName ?? a.agentId}
                  </span>
                </div>
                <span className="ml-2 shrink-0 font-mono font-semibold">{formatUsd(a.costUsd)}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}
    </div>
  )
}

function KpiCard({
  label,
  value,
  secondary,
}: {
  label: string
  value: string
  secondary?: string
}) {
  return (
    <Card padded>
      <p className="text-[11px] font-semibold uppercase tracking-wider text-fg-dim">{label}</p>
      <p className="mt-1 text-2xl font-semibold tracking-tight text-fg">{value}</p>
      {secondary && <p className="mt-1 text-xs text-fg-muted">{secondary}</p>}
    </Card>
  )
}

interface GranularityToggleProps {
  value: Granularity
  onChange: (next: Granularity) => void
}

function GranularityToggle({ value, onChange }: GranularityToggleProps) {
  return (
    <div className="inline-flex shrink-0 rounded-lg border border-border bg-surface p-0.5">
      {(['day', 'hour'] as const).map((g) => (
        <button
          key={g}
          type="button"
          onClick={() => onChange(g)}
          className={cn(
            'rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
            value === g ? 'bg-accent text-accent-contrast' : 'text-fg-muted hover:text-fg',
          )}
          aria-pressed={value === g}
        >
          {g === 'day' ? 'Dia' : 'Hora'}
        </button>
      ))}
    </div>
  )
}

interface ChartTypeToggleProps {
  value: ChartType
  onChange: (next: ChartType) => void
}

function ChartTypeToggle({ value, onChange }: ChartTypeToggleProps) {
  return (
    <div className="inline-flex shrink-0 rounded-lg border border-border bg-surface p-0.5">
      {(['line', 'bar'] as const).map((t) => (
        <button
          key={t}
          type="button"
          onClick={() => onChange(t)}
          className={cn(
            'rounded-md px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
            value === t ? 'bg-accent text-accent-contrast' : 'text-fg-muted hover:text-fg',
          )}
          aria-pressed={value === t}
        >
          {t === 'line' ? 'Linha' : 'Barras'}
        </button>
      ))}
    </div>
  )
}

interface SparkProps {
  buckets: ProjectTimeseriesBucket[]
  mode: ChartType
}

function Spark({ buckets, mode }: SparkProps) {
  if (buckets.length === 0) {
    return (
      <p className="rounded-md bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">
        Sem execuções no período.
      </p>
    )
  }

  // 2 séries renderizadas com escalas independentes — máximo de cada uma
  // mapeia pra 100% da altura visual (linha ou barra). Custo em accent (azul);
  // execuções (incluindo falhas como overlay vermelho na barra; linha vermelha
  // segue a curva de falhas no modo linha).
  const maxCost = Math.max(...buckets.map((b) => b.costUsd), 0.0001)
  const maxExecs = Math.max(...buckets.map((b) => b.executions), 1)
  const maxFailed = Math.max(...buckets.map((b) => b.failed), 0)

  return (
    <div className="space-y-3">
      {mode === 'bar' ? (
        <SparkBars buckets={buckets} maxCost={maxCost} />
      ) : (
        <SparkLines buckets={buckets} maxCost={maxCost} maxFailed={maxFailed} />
      )}

      {/* Eixo X: rotula primeiro, meio, último — evita poluir com 30 datas. */}
      <div className="flex justify-between text-[10px] text-fg-dim">
        <span>{formatBucket(buckets[0].bucket)}</span>
        {buckets.length > 2 && (
          <span>{formatBucket(buckets[Math.floor(buckets.length / 2)].bucket)}</span>
        )}
        <span>{formatBucket(buckets[buckets.length - 1].bucket)}</span>
      </div>

      <div className="flex flex-wrap items-center gap-4 text-[11px] text-fg-muted">
        <Legend color="bg-accent" label={`custo (máx ${formatUsd(maxCost)})`} />
        {(mode === 'line' || maxFailed > 0) && (
          <Legend color="bg-warning" label={`falhas (máx ${maxExecs} execs)`} />
        )}
      </div>
    </div>
  )
}

interface SparkBarsProps {
  buckets: ProjectTimeseriesBucket[]
  maxCost: number
}

function SparkBars({ buckets, maxCost }: SparkBarsProps) {
  return (
    <div className="flex h-40 items-end gap-1">
      {buckets.map((b, idx) => {
        const costPct = (b.costUsd / maxCost) * 100
        const errPct = b.executions > 0 ? (b.failed / b.executions) * 100 : 0
        return (
          <div key={idx} className="group relative flex h-full flex-1 items-end">
            <div
              className="w-full overflow-hidden rounded-t-sm bg-accent transition-opacity group-hover:opacity-80"
              style={{ height: `${Math.max(costPct, 2)}%` }}
              title={`${formatBucket(b.bucket)} — ${formatUsd(b.costUsd)} / ${formatNumber(b.executions)} execuções (${b.failed} falha${b.failed === 1 ? '' : 's'})`}
            >
              {errPct > 0 && (
                <div
                  className="bg-warning"
                  style={{ height: `${errPct}%` }}
                  aria-hidden="true"
                />
              )}
            </div>
          </div>
        )
      })}
    </div>
  )
}

interface SparkLinesProps {
  buckets: ProjectTimeseriesBucket[]
  maxCost: number
  maxFailed: number
}

function SparkLines({ buckets, maxCost, maxFailed }: SparkLinesProps) {
  // SVG com viewBox 100x100 + preserveAspectRatio="none" estica pra largura/
  // altura do container CSS. Coordenadas em % (0..100) facilitam o cálculo.
  const W = 100
  const H = 100
  const n = buckets.length
  const xAt = (i: number) => (n === 1 ? W / 2 : (i / (n - 1)) * W)

  const costPoints = buckets
    .map((b, i) => `${xAt(i).toFixed(2)},${(H - (b.costUsd / maxCost) * H).toFixed(2)}`)
    .join(' ')
  const failPoints = maxFailed > 0
    ? buckets
        .map((b, i) => `${xAt(i).toFixed(2)},${(H - (b.failed / maxFailed) * H).toFixed(2)}`)
        .join(' ')
    : ''
  // Área sob a curva de custo — ponto inicial no chão, percorre a linha,
  // fecha no chão à direita. Cria sensação de volume sem ser pesado.
  const costArea = `0,${H} ${costPoints} ${W},${H}`

  return (
    <div className="relative h-40 w-full">
      <svg
        viewBox={`0 0 ${W} ${H}`}
        preserveAspectRatio="none"
        className="absolute inset-0 h-full w-full"
        aria-hidden="true"
      >
        {/* Grade horizontal sutil — 25% / 50% / 75% */}
        {[25, 50, 75].map((y) => (
          <line key={y} x1={0} x2={W} y1={y} y2={y} stroke="currentColor" strokeWidth={0.2} className="text-border" />
        ))}

        {/* Área de custo */}
        <polyline
          points={costArea}
          fill="currentColor"
          className="text-accent/15"
          stroke="none"
        />
        {/* Linha de custo */}
        <polyline
          points={costPoints}
          fill="none"
          stroke="currentColor"
          strokeWidth={1.2}
          strokeLinecap="round"
          strokeLinejoin="round"
          className="text-accent"
          vectorEffect="non-scaling-stroke"
        />
        {/* Linha de falhas (só se houver pelo menos 1 falha no período) */}
        {failPoints && (
          <polyline
            points={failPoints}
            fill="none"
            stroke="currentColor"
            strokeWidth={1}
            strokeDasharray="2 2"
            className="text-warning"
            vectorEffect="non-scaling-stroke"
          />
        )}
      </svg>

      {/* Pontos invisíveis com title pro tooltip nativo. Posicionamento absoluto
          em % bate com o SVG (mesma escala). */}
      {buckets.map((b, i) => {
        const left = (xAt(i) / W) * 100
        const top = (1 - b.costUsd / maxCost) * 100
        return (
          <div
            key={i}
            className="absolute h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full bg-accent ring-2 ring-surface opacity-0 transition-opacity hover:opacity-100"
            style={{ left: `${left}%`, top: `${top}%` }}
            title={`${formatBucket(b.bucket)} — ${formatUsd(b.costUsd)} / ${formatNumber(b.executions)} execuções (${b.failed} falha${b.failed === 1 ? '' : 's'})`}
          />
        )
      })}
    </div>
  )
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={cn('h-2 w-3 rounded-sm', color)} aria-hidden="true" />
      {label}
    </span>
  )
}

interface AgentsTableProps {
  rows: ProjectAgentBreakdown[]
}

function AgentsTable({ rows }: AgentsTableProps) {
  if (rows.length === 0) {
    return (
      <p className="rounded-md bg-bg-soft px-3 py-4 text-center text-xs text-fg-muted">
        Sem dados de uso por agente no período.
      </p>
    )
  }
  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="border-b border-border text-[10px] uppercase tracking-wider text-fg-dim">
            <th className="py-2 pr-3 text-left font-semibold">Agente</th>
            <th className="py-2 px-3 text-left font-semibold">Modelo</th>
            <th className="py-2 px-3 text-right font-semibold">Calls</th>
            <th className="py-2 px-3 text-right font-semibold">Tokens</th>
            <th className="py-2 px-3 text-right font-semibold">Custo</th>
            <th className="py-2 px-3 text-right font-semibold">Avg</th>
            <th className="py-2 px-3 text-right font-semibold">p95</th>
            <th className="py-2 pl-3 text-right font-semibold">Erro</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.agentId} className="border-b border-border last:border-b-0 hover:bg-bg-soft">
              {/* Nome quando disponível; agentId mono (cinza) em segunda linha
                  como referência técnica. Fallback pro id quando o agent foi
                  deletado e ainda tem consumo histórico no período. */}
              <td className="py-2 pr-3" title={r.agentId}>
                {r.agentName ? (
                  <>
                    <div className="text-fg">{r.agentName}</div>
                    <div className="font-mono text-[10px] text-fg-dim">{r.agentId}</div>
                  </>
                ) : (
                  <span className="font-mono text-fg">{r.agentId}</span>
                )}
              </td>
              <td className="py-2 px-3 text-fg-muted">{r.modelId ?? '—'}</td>
              <td className="py-2 px-3 text-right font-mono">{formatNumber(r.calls)}</td>
              <td className="py-2 px-3 text-right font-mono">{formatNumber(r.totalTokens)}</td>
              <td className="py-2 px-3 text-right font-mono font-semibold">
                {formatUsd(r.costUsd)}
              </td>
              <td className="py-2 px-3 text-right font-mono">
                {Math.round(r.avgDurationMs)} ms
              </td>
              <td className="py-2 px-3 text-right font-mono">
                {Math.round(r.p95DurationMs)} ms
              </td>
              <td
                className={cn(
                  'py-2 pl-3 text-right font-mono',
                  r.errorRate > 0.1 ? 'text-warning' : 'text-fg-muted',
                )}
              >
                {(r.errorRate * 100).toFixed(1)}%
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

interface BudgetCardProps {
  status: ProjectBudgetStatus
}

function BudgetCard({ status }: BudgetCardProps) {
  const noBudget =
    status.maxCostUsdPerDay == null && status.maxTokensPerDay == null
  return (
    <Card className="space-y-3">
      <CardHeader
        title="Orçamento diário"
        description={
          noBudget
            ? 'Nenhum limite diário configurado neste projeto.'
            : 'Consumo de hoje vs limite configurado em ProjectSettings. Atenção: hoje a plataforma só registra alerta — execução não é bloqueada quando o limite é cruzado.'
        }
      />

      {!noBudget && (
        <div className="space-y-3">
          <BudgetBar
            label="Custo USD"
            current={status.todayCostUsd}
            max={status.maxCostUsdPerDay ?? null}
            pct={status.costUsagePct ?? null}
            format={formatUsd}
          />
          <BudgetBar
            label="Tokens"
            current={status.todayTokens}
            max={status.maxTokensPerDay ?? null}
            pct={status.tokensUsagePct ?? null}
            format={formatNumber}
          />
        </div>
      )}

      {status.exceeded && (
        <div className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          Limite diário cruzado. As execuções continuam acontecendo — esse é um sinal pra revisar o
          uso e ajustar limite ou desligar agentes específicos manualmente.
        </div>
      )}

      {!status.exceeded && !noBudget && (
        <Badge tone="success">Dentro do orçamento</Badge>
      )}
    </Card>
  )
}

function BudgetBar({
  label,
  current,
  max,
  pct,
  format,
}: {
  label: string
  current: number
  max: number | null
  pct: number | null
  format: (n: number) => string
}) {
  if (max == null || max === 0) {
    return (
      <div>
        <div className="flex items-baseline justify-between text-xs">
          <span className="font-semibold text-fg">{label}</span>
          <span className="font-mono text-fg-muted">{format(current)} hoje · sem limite</span>
        </div>
      </div>
    )
  }
  const safePct = Math.min(Math.max(pct ?? 0, 0), 1)
  const overshoot = (pct ?? 0) > 1
  return (
    <div>
      <div className="mb-1 flex items-baseline justify-between text-xs">
        <span className="font-semibold text-fg">{label}</span>
        <span className="font-mono text-fg-muted">
          {format(current)} / {format(max)} ({((pct ?? 0) * 100).toFixed(0)}%)
        </span>
      </div>
      <div className="h-2 overflow-hidden rounded-full bg-bg-soft">
        <div
          className={cn(
            'h-full transition-all',
            overshoot ? 'bg-warning' : safePct > 0.8 ? 'bg-warning' : 'bg-accent',
          )}
          style={{ width: `${Math.max(safePct * 100, overshoot ? 100 : 0)}%` }}
          aria-hidden="true"
        />
      </div>
    </div>
  )
}

// Utilitários de formatação --------------------------------------------------

function formatUsd(n: number): string {
  if (n === 0) return '$0.00'
  if (n < 0.01) return `$${n.toFixed(4)}`
  if (n < 1) return `$${n.toFixed(3)}`
  return `$${n.toFixed(2)}`
}

function formatNumber(n: number): string {
  if (n < 1000) return String(n)
  return n.toLocaleString('pt-BR')
}

function formatBucket(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit' })
}
