/**
 * BarChart — barras verticais ou horizontais sobre categorias.
 *
 * Reusabilidade: domain-agnostic. Recebe dados já agregados pelo caller
 * (top N, soma por categoria). NÃO faz ordenação automática — caller controla
 * pra que a ordem case com tabelas correspondentes na mesma tela.
 *
 * Layout: 'horizontal' (barras horizontais, ideal pra labels longos como
 * nomes de agente) ou 'vertical' (categorias no eixo x). Default horizontal.
 *
 * NÃO faz: agrupamento (stacked), múltiplas séries (uma cor única — quem
 * precisa de comparação multi-série usa TimeSeriesChart com bucket=label).
 *
 * Exemplo: <BarChart data={topAgents} dataKey="costUsd" labelKey="agentName" format="currency" />
 *          Reusado por: futuras telas de reliability (error rate por agente).
 */
import {
  Bar,
  CartesianGrid,
  BarChart as RechartsBarChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
  type TooltipProps,
} from 'recharts'
import { EmptyState } from '../ui/EmptyState'
import {
  formatCurrency,
  formatCurrencyCompact,
  formatInt,
  formatIntCompact,
} from '../../utils/format'

export type BarFormat = 'currency' | 'integer'

interface BarChartProps<T> {
  data: ReadonlyArray<T>
  /** Chave numérica do valor. */
  dataKey: keyof T & string
  /** Chave string do label visível. */
  labelKey: keyof T & string
  format: BarFormat
  layout?: 'horizontal' | 'vertical'
  height?: number
  color?: string
  emptyTitle?: string
}

function formatValue(format: BarFormat, v: number, compact: boolean): string {
  if (format === 'currency') return compact ? formatCurrencyCompact(v) : formatCurrency(v)
  return compact ? formatIntCompact(v) : formatInt(v)
}

function BarTooltip(props: TooltipProps<number, string> & { format: BarFormat }) {
  const { active, payload, format } = props
  if (!active || !payload || payload.length === 0) return null
  const entry = payload[0]
  const num = typeof entry.value === 'number' ? entry.value : Number(entry.value)
  return (
    <div className="rounded-lg border border-border bg-surface px-3 py-2 text-xs shadow-soft">
      <p className="font-medium text-fg">{entry.payload.__label}</p>
      <p className="text-fg-muted">{formatValue(format, num, false)}</p>
    </div>
  )
}

export function BarChart<T>({
  data,
  dataKey,
  labelKey,
  format,
  layout = 'horizontal',
  height = 240,
  color = 'rgb(var(--color-accent))',
  emptyTitle = 'Sem dados pra mostrar',
}: BarChartProps<T>): React.ReactElement {
  if (!data || data.length === 0) {
    return <EmptyState title={emptyTitle} />
  }

  // Recharts precisa do label num campo previsível pra tooltip. Mapeia pra
  // `__label` evitando colisão com chaves do caller.
  const normalized = data.map((row) => ({
    ...row,
    __label: String((row as Record<string, unknown>)[labelKey] ?? '—'),
  }))

  const horizontal = layout === 'horizontal'

  return (
    <div style={{ width: '100%', height }}>
      <ResponsiveContainer>
        <RechartsBarChart
          data={normalized}
          layout={horizontal ? 'vertical' : 'horizontal'}
          margin={{ top: 8, right: 16, left: 8, bottom: 0 }}
        >
          <CartesianGrid stroke="rgb(var(--color-border))" strokeDasharray="3 3" />
          {horizontal ? (
            <>
              <XAxis
                type="number"
                stroke="rgb(var(--color-fg-dim))"
                tick={{ fontSize: 11 }}
                tickFormatter={(v: number) => formatValue(format, v, true)}
              />
              <YAxis
                type="category"
                dataKey="__label"
                stroke="rgb(var(--color-fg-dim))"
                tick={{ fontSize: 11 }}
                width={140}
              />
            </>
          ) : (
            <>
              <XAxis
                type="category"
                dataKey="__label"
                stroke="rgb(var(--color-fg-dim))"
                tick={{ fontSize: 11 }}
              />
              <YAxis
                type="number"
                stroke="rgb(var(--color-fg-dim))"
                tick={{ fontSize: 11 }}
                tickFormatter={(v: number) => formatValue(format, v, true)}
              />
            </>
          )}
          <Tooltip
            content={(p) => <BarTooltip {...(p as TooltipProps<number, string>)} format={format} />}
            cursor={{ fill: 'rgb(var(--color-accent) / 0.06)' }}
          />
          <Bar dataKey={dataKey} fill={color} radius={[4, 4, 4, 4]} isAnimationActive={false} />
        </RechartsBarChart>
      </ResponsiveContainer>
    </div>
  )
}
