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
import { listAgents } from '../api/agents'
import { ApiError, friendlyError } from '../api/client'
import { getIdentity } from '../stores/identity'
import { isInCurrentProject } from '../stores/projectScope'
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
  /** Agentes ocultos da tabela (Visibility=global, projeto-dono diferente). */
  hiddenAgents: ProjectAgentBreakdown[]
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
      // Fase 1 (paralelo): descobrir quais IDs de agente são "internos da
      // plataforma" (visibility=global pertencendo a outro projeto). Isso
      // alimenta a serialização da Fase 2 — timeseries usa excludeAgentIds
      // pra descontar consumo desses no backend (LLM agg via JOIN).
      const [agents, allAgents] = await Promise.all([
        getProjectAgents(projectId, 20),
        listAgents(),
      ])
      const ownAgentIds = new Set(allAgents.filter(isInCurrentProject).map((a) => a.id))
      const visibleAgents = agents.filter((a) => ownAgentIds.has(a.agentId))
      const hiddenAgents = agents.filter((a) => !ownAgentIds.has(a.agentId))
      const hiddenIds = hiddenAgents.map((a) => a.agentId)

      // Fase 2 (paralelo): overview/timeseries/budget. Timeseries recebe
      // excludeAgentIds pra que cost/tokens/calls dos buckets já venham
      // descontados do backend.
      const [overview, timeseries, budget] = await Promise.all([
        getProjectOverview(projectId),
        getProjectTimeseries(projectId, signalGranularity, undefined, undefined, hiddenIds),
        getProjectBudget(projectId),
      ])
      // Desconta os hidden dos totais. cost/tokens/calls são diretamente
      // somados; execuções/completed/failed são aproximados via calls (cada
      // workflow do assistente-perfil/gerador-testcases é 1 execution = 1 call,
      // razão prática válida pra workflows simples Graph single-agent).
      const sub = hiddenAgents.reduce(
        (acc, a) => {
          acc.cost += a.costUsd
          acc.tokens += a.totalTokens
          acc.calls += a.calls
          // Falhas = errorRate * calls; sucessos = calls - falhas.
          const fail = Math.round(a.calls * a.errorRate)
          acc.failed += fail
          acc.completed += Math.max(0, a.calls - fail)
          return acc
        },
        { cost: 0, tokens: 0, calls: 0, completed: 0, failed: 0 },
      )
      const adjCompleted = Math.max(0, overview.completed - sub.completed)
      const adjFailed = Math.max(0, overview.failed - sub.failed)
      const totalResolved = adjCompleted + adjFailed
      const adjustedOverview: ProjectOverview = {
        ...overview,
        totalCostUsd: Math.max(0, overview.totalCostUsd - sub.cost),
        totalTokens: Math.max(0, overview.totalTokens - sub.tokens),
        totalCalls: Math.max(0, overview.totalCalls - sub.calls),
        totalExecutions: Math.max(0, overview.totalExecutions - sub.calls),
        completed: adjCompleted,
        failed: adjFailed,
        successRate: totalResolved > 0 ? adjCompleted / totalResolved : 0,
        topAgents: overview.topAgents.filter((a) => ownAgentIds.has(a.agentId)),
      }
      setData({
        overview: adjustedOverview,
        timeseries,
        agents: visibleAgents,
        budget,
        hiddenAgents,
      })
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
            {data.hiddenAgents.length > 0 && (
              <p className="text-[11px] leading-relaxed text-fg-dim">
                Custo, tokens e LLM calls já descontam os {data.hiddenAgents.length} agente
                {data.hiddenAgents.length === 1 ? '' : 's'} interno{data.hiddenAgents.length === 1 ? '' : 's'} da plataforma.
                A contagem de execuções/falhas é por workflow (granularidade diferente) — pode incluir
                runs do assistente de perfil/gerador de test cases.
              </p>
            )}
          </Card>

          <Card className="space-y-4">
            <CardHeader
              title="Agentes do projeto"
              description="Top 20 por custo no período. p95 reflete a duração das chamadas LLM no agente; error rate é por execução do workflow envolvendo o agente."
            />
            <AgentsTable rows={data.agents} />
            {data.hiddenAgents.length > 0 && (
              <p className="text-[11px] leading-relaxed text-fg-dim">
                {data.hiddenAgents.length} agente
                {data.hiddenAgents.length === 1 ? '' : 's'} interno
                {data.hiddenAgents.length === 1 ? '' : 's'} da plataforma oculto
                {data.hiddenAgents.length === 1 ? '' : 's'} (assistente de perfil, gerador de test
                cases). Consumo de <strong>${data.hiddenAgents.reduce((sum, a) => sum + a.costUsd, 0).toFixed(4)}</strong>{' '}
                já foi descontado dos cards e da taxa de sucesso acima.
              </p>
            )}
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
              >
                <div className="flex min-w-0 items-center gap-2">
                  <span className="flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent-subtle text-[10px] font-bold text-accent">
                    {idx + 1}
                  </span>
                  <span className="truncate font-mono">{a.agentId}</span>
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
              <td className="py-2 pr-3 font-mono text-fg">{r.agentId}</td>
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
