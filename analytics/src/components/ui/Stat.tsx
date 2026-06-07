/**
 * Stat — bloco KPI: label + valor grande + delta opcional + sparkline opcional.
 *
 * Reusabilidade: domain-agnostic. Recebe `value` já formatado (string) pra que
 * caller controle locale/casas decimais via utils/format. `value === null`
 * sinaliza loading (renderiza Skeleton); `value === '—'` sinaliza ausência
 * real de dado (backend devolveu null/zero conhecido).
 *
 * Delta: número positivo → verde, negativo → vermelho, zero/null → omitido.
 * Sparkline: array de números puros, sem timestamps — recharts internamente
 * usa o índice como x. Pra visualizações com x significativo, use TimeSeriesChart.
 *
 * NÃO faz: requisição própria (todo dado vem por props), comparação automática
 * com período anterior (caller calcula o delta).
 *
 * Exemplo: <Stat label="Custo MTD" value={formatCurrency(o.totalCostUsd)} />
 *          Reusado por: Overview KPIs, futuros painéis de Reliability.
 */
import { Line, LineChart, ResponsiveContainer } from 'recharts'
import { cn } from './cn'
import { Skeleton } from './Skeleton'

interface StatProps {
  label: string
  /**
   * Valor formatado pra exibição. `null` = estado de loading (mostra Skeleton).
   * String vazia ou '—' = dado conhecidamente ausente (não dispara skeleton).
   */
  value: string | null
  /** Variação relativa em pontos percentuais. Sign drives a cor. */
  delta?: number | null
  /** Texto auxiliar abaixo do valor (ex.: "vs. mês anterior"). */
  hint?: string
  /** Série temporal mínima pra desenhar tendência. Recharts usa índice como x. */
  trend?: ReadonlyArray<number>
  className?: string
}

function DeltaBadge({ delta }: { delta: number }) {
  // Zero exato omitido pela renderização — chega aqui só não-zero.
  const positive = delta > 0
  const arrow = positive ? '▲' : '▼'
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-md px-1.5 py-0.5 text-[11px] font-medium',
        positive ? 'bg-success/10 text-success' : 'bg-danger/10 text-danger',
      )}
    >
      {arrow} {Math.abs(delta).toFixed(1).replace('.', ',')}%
    </span>
  )
}

function Sparkline({ trend }: { trend: ReadonlyArray<number> }) {
  // Recharts só aceita objects — converte ao mínimo necessário. Renderização
  // sem eixos/tooltips: a leitura é qualitativa (subiu/desceu).
  const data = trend.map((v, i) => ({ i, v }))
  return (
    <div className="h-10 w-24">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={data} margin={{ top: 4, bottom: 4, left: 0, right: 0 }}>
          <Line
            type="monotone"
            dataKey="v"
            stroke="rgb(var(--color-accent))"
            strokeWidth={1.5}
            dot={false}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  )
}

export function Stat({ label, value, delta, hint, trend, className }: StatProps) {
  const showDelta = typeof delta === 'number' && Number.isFinite(delta) && delta !== 0
  return (
    <div className={cn('flex flex-col gap-1', className)}>
      <span className="text-xs font-medium uppercase tracking-wide text-fg-muted">{label}</span>
      <div className="flex items-end justify-between gap-3">
        <div className="min-w-0">
          {value === null ? (
            <Skeleton className="h-7 w-28" />
          ) : (
            <span className="block truncate text-2xl font-semibold text-fg">{value}</span>
          )}
          <div className="mt-1 flex items-center gap-2">
            {showDelta && <DeltaBadge delta={delta} />}
            {hint && <span className="text-[11px] text-fg-dim">{hint}</span>}
          </div>
        </div>
        {trend && trend.length > 1 && <Sparkline trend={trend} />}
      </div>
    </div>
  )
}
