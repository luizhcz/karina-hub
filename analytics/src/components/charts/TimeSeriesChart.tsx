/**
 * TimeSeriesChart — gráfico de linha/área multi-série sobre buckets temporais.
 *
 * Reusabilidade: domain-agnostic. Caller decide o shape de dado, fornece:
 *   - `data`: array com chave temporal `bucket` (ISO string).
 *   - `series`: descrição de cada linha — dataKey (path no objeto), label
 *     visível, formato do valor (currency, integer, ...) e cor opcional.
 * Recharts não consegue formatar inline com locale pt-BR — fazemos pelos
 * formatters do util/format.
 *
 * Decisões:
 *   - Variant `area` pra séries acumulativas (tokens), `line` pra rates (custo).
 *   - Y-axis hidden por default — em painéis pequenos rouba área. Caller pode
 *     reativar via prop showYAxis.
 *   - Multi-série usa eixos compartilhados quando os formatters batem;
 *     quando há mistura (tokens em milhares, custo em USD), passar
 *     `dualAxis` true e o componente cria 2 eixos Y.
 *
 * NÃO faz: fetch, agregação client-side (já vem do backend em buckets),
 * legend custom (usa default do recharts).
 *
 * Exemplo: <TimeSeriesChart data={ts} series={[{ dataKey: 'costUsd', label: 'Custo', format: 'currency' }, ...]} />
 *          Reusado por: Overview chart, Custos throughput, futuras telas.
 */
import {
  Area,
  AreaChart,
  CartesianGrid,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type TooltipProps,
} from 'recharts'
import { EmptyState } from '../ui/EmptyState'
import {
  formatBucketLabel,
  formatCurrencyCompact,
  formatIntCompact,
  formatCurrency,
  formatInt,
} from '../../utils/format'

export type SeriesFormat = 'currency' | 'integer'

export interface ChartSeries<T> {
  /** Chave no objeto de data. Tipada via keyof pra evitar typos. */
  dataKey: keyof T & string
  /** Label visível no tooltip e na legenda. */
  label: string
  /** Formato do número — define o formatter usado no tooltip. */
  format: SeriesFormat
  /** Cor opcional. Default cicla entre accent/success/warning. */
  color?: string
  /** Quando dualAxis=true, define em qual eixo a série renderiza. */
  yAxisId?: 'left' | 'right'
}

interface TimeSeriesChartProps<T extends { bucket: string }> {
  data: ReadonlyArray<T>
  series: ReadonlyArray<ChartSeries<T>>
  granularity: 'day' | 'hour'
  variant?: 'line' | 'area'
  height?: number
  /** Quando true, renderiza 2 eixos Y. Use quando mistura currency + count. */
  dualAxis?: boolean
  showYAxis?: boolean
  /** Mensagem de empty state customizada. */
  emptyTitle?: string
  emptyDescription?: string
}

const DEFAULT_COLORS = [
  'rgb(var(--color-accent))',
  'rgb(var(--color-success))',
  'rgb(var(--color-warning))',
]

function formatValue(format: SeriesFormat, value: number, compact: boolean): string {
  if (format === 'currency') {
    return compact ? formatCurrencyCompact(value) : formatCurrency(value)
  }
  return compact ? formatIntCompact(value) : formatInt(value)
}

function ChartTooltip<T extends { bucket: string }>(
  props: TooltipProps<number, string> & {
    series: ReadonlyArray<ChartSeries<T>>
    granularity: 'day' | 'hour'
  },
) {
  const { active, payload, label, series, granularity } = props
  if (!active || !payload || payload.length === 0) return null
  const bucketIso = typeof label === 'string' ? label : String(label)
  return (
    <div className="rounded-lg border border-border bg-surface px-3 py-2 text-xs shadow-soft">
      <p className="mb-1 font-medium text-fg">{formatBucketLabel(bucketIso, granularity)}</p>
      <div className="flex flex-col gap-0.5">
        {payload.map((entry) => {
          const s = series.find((x) => x.dataKey === entry.dataKey)
          if (!s) return null
          const num = typeof entry.value === 'number' ? entry.value : Number(entry.value)
          return (
            <div key={s.dataKey} className="flex items-center gap-2">
              <span className="h-2 w-2 rounded-full" style={{ background: entry.color }} />
              <span className="text-fg-muted">{s.label}:</span>
              <span className="font-medium text-fg">{formatValue(s.format, num, false)}</span>
            </div>
          )
        })}
      </div>
    </div>
  )
}

export function TimeSeriesChart<T extends { bucket: string }>(
  props: TimeSeriesChartProps<T>,
): React.ReactElement {
  const {
    data,
    series,
    granularity,
    variant = 'line',
    height = 240,
    dualAxis = false,
    showYAxis = false,
    emptyTitle = 'Sem dados no período',
    emptyDescription = 'Tente um intervalo maior ou outro projeto.',
  } = props

  if (!data || data.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />
  }

  const ChartImpl = variant === 'area' ? AreaChart : LineChart

  return (
    <div style={{ width: '100%', height }}>
      <ResponsiveContainer>
        <ChartImpl data={data as T[]} margin={{ top: 8, right: 16, left: 4, bottom: 0 }}>
          <CartesianGrid stroke="rgb(var(--color-border))" strokeDasharray="3 3" vertical={false} />
          <XAxis
            dataKey="bucket"
            tickFormatter={(iso: string) => formatBucketLabel(iso, granularity)}
            stroke="rgb(var(--color-fg-dim))"
            tick={{ fontSize: 11 }}
            minTickGap={24}
          />
          {/*
            Eixos Y. Recharts exige que TODO yAxisId referenciado por Line/Area
            tenha um <YAxis yAxisId> correspondente montado — senão dispara
            "Invariant failed". Por isso:
              - dualAxis=true → SEMPRE renderiza ambos left+right (hide
                controla só a apresentação visual; o eixo continua resolvendo
                escala).
              - dualAxis=false + showYAxis=true → renderiza só left.
              - dualAxis=false + showYAxis=false → omite ambos; Lines/Areas
                ficam com yAxisId=undefined e o recharts cria o default
                implícito.
          */}
          {(showYAxis || dualAxis) && (
            <YAxis
              yAxisId="left"
              hide={!showYAxis}
              stroke="rgb(var(--color-fg-dim))"
              tick={{ fontSize: 11 }}
              tickFormatter={(v: number) =>
                formatValue(
                  series.find((s) => (s.yAxisId ?? 'left') === 'left')?.format ?? 'integer',
                  v,
                  true,
                )
              }
            />
          )}
          {dualAxis && (
            <YAxis
              yAxisId="right"
              orientation="right"
              hide={!showYAxis}
              stroke="rgb(var(--color-fg-dim))"
              tick={{ fontSize: 11 }}
              tickFormatter={(v: number) =>
                formatValue(
                  series.find((s) => s.yAxisId === 'right')?.format ?? 'integer',
                  v,
                  true,
                )
              }
            />
          )}
          <Tooltip
            content={(p) => (
              <ChartTooltip {...(p as TooltipProps<number, string>)} series={series} granularity={granularity} />
            )}
            cursor={{ stroke: 'rgb(var(--color-border-strong))', strokeWidth: 1 }}
          />
          {series.map((s, i) => {
            const color = s.color ?? DEFAULT_COLORS[i % DEFAULT_COLORS.length]
            const yAxisId = dualAxis ? s.yAxisId ?? 'left' : showYAxis ? 'left' : undefined
            if (variant === 'area') {
              return (
                <Area
                  key={s.dataKey}
                  type="monotone"
                  dataKey={s.dataKey}
                  name={s.label}
                  stroke={color}
                  fill={color}
                  fillOpacity={0.18}
                  strokeWidth={2}
                  yAxisId={yAxisId}
                  isAnimationActive={false}
                />
              )
            }
            return (
              <Line
                key={s.dataKey}
                type="monotone"
                dataKey={s.dataKey}
                name={s.label}
                stroke={color}
                strokeWidth={2}
                dot={false}
                yAxisId={yAxisId}
                isAnimationActive={false}
              />
            )
          })}
        </ChartImpl>
      </ResponsiveContainer>
    </div>
  )
}
