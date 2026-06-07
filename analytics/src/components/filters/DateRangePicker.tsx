/**
 * DateRangePicker — controlled, com presets + range custom.
 *
 * Reusabilidade: domain-agnostic. Não conhece useDateRange — recebe `range`
 * + callbacks e devolve eventos. Caller (Overview, Custos, futura tela)
 * conecta com o hook. Isso permite testar componentes isolados e reusar o
 * picker em filtros que não precisam de URL sync.
 *
 * NÃO faz: validação de "to >= from" (visualmente pode ficar incoerente por
 * 1 frame durante typing; backend já barra). Time zone picker (sempre UTC).
 *
 * Exemplo: <DateRangePicker range={range} onPresetChange={setPreset} onCustomChange={setCustom} />
 *          Reusado por: todo header de filtro de tela analytics.
 */
import { useCallback, useMemo } from 'react'
import { DATE_RANGE_PRESETS, type DateRange, type DateRangePreset } from '../../hooks/useDateRange'

interface DateRangePickerProps {
  range: DateRange
  onPresetChange: (preset: DateRangePreset) => void
  onCustomChange: (from: Date, to: Date) => void
}

/** Converte Date pra string "YYYY-MM-DD" usando UTC (consistente c/ backend). */
function toInputDate(d: Date): string {
  const y = d.getUTCFullYear()
  const m = String(d.getUTCMonth() + 1).padStart(2, '0')
  const day = String(d.getUTCDate()).padStart(2, '0')
  return `${y}-${m}-${day}`
}

/** Lê string "YYYY-MM-DD" como meia-noite UTC (consistente c/ backend). */
function fromInputDate(s: string, fallback: Date): Date {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s)
  if (!m) return fallback
  const [, y, mo, d] = m
  return new Date(Date.UTC(Number(y), Number(mo) - 1, Number(d)))
}

export function DateRangePicker({ range, onPresetChange, onCustomChange }: DateRangePickerProps) {
  const isCustom = range.preset === 'custom'

  const fromStr = useMemo(() => toInputDate(range.from), [range.from])
  const toStr = useMemo(() => toInputDate(range.to), [range.to])

  const handleFromChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const nextFrom = fromInputDate(e.target.value, range.from)
      onCustomChange(nextFrom, range.to)
    },
    [onCustomChange, range.from, range.to],
  )

  const handleToChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const nextTo = fromInputDate(e.target.value, range.to)
      onCustomChange(range.from, nextTo)
    },
    [onCustomChange, range.from, range.to],
  )

  return (
    <div className="flex flex-wrap items-end gap-3">
      <div className="flex flex-col gap-1">
        <label className="text-xs font-medium text-fg-muted" htmlFor="range-preset">
          Período
        </label>
        <select
          id="range-preset"
          value={range.preset}
          onChange={(e) => onPresetChange(e.target.value as DateRangePreset)}
          className="h-9 rounded-lg border border-border bg-surface px-3 text-sm text-fg focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
        >
          {DATE_RANGE_PRESETS.map((p) => (
            <option key={p.value} value={p.value}>
              {p.label}
            </option>
          ))}
        </select>
      </div>

      {isCustom && (
        <>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-fg-muted" htmlFor="range-from">
              De
            </label>
            <input
              id="range-from"
              type="date"
              value={fromStr}
              onChange={handleFromChange}
              className="h-9 rounded-lg border border-border bg-surface px-3 text-sm text-fg focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            />
          </div>
          <div className="flex flex-col gap-1">
            <label className="text-xs font-medium text-fg-muted" htmlFor="range-to">
              Até
            </label>
            <input
              id="range-to"
              type="date"
              value={toStr}
              onChange={handleToChange}
              className="h-9 rounded-lg border border-border bg-surface px-3 text-sm text-fg focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30"
            />
          </div>
        </>
      )}
    </div>
  )
}
