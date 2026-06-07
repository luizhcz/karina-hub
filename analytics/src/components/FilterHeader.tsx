/**
 * FilterHeader — agrupa ProjectPicker + DateRangePicker. Único componente
 * reusado pelas telas Overview e Custos pra que mover o seletor de projeto
 * de lugar (futuramente um sidebar global) seja uma edição local.
 *
 * Reusabilidade: assume que existe um `useDateRange` no caller e cospe o
 * range. ProjectPicker é singleton (sempre lê identity global), então não
 * precisa de props.
 *
 * NÃO faz: estado de filtros avançados (modelo, ownedOnly) — quando vierem,
 * componente novo embaixo desse.
 */
import { DateRangePicker } from './filters/DateRangePicker'
import { ProjectPicker } from './filters/ProjectPicker'
import type { DateRange, DateRangePreset } from '../hooks/useDateRange'

interface FilterHeaderProps {
  range: DateRange
  onPresetChange: (preset: DateRangePreset) => void
  onCustomChange: (from: Date, to: Date) => void
}

export function FilterHeader({ range, onPresetChange, onCustomChange }: FilterHeaderProps) {
  return (
    <div className="flex flex-wrap items-end gap-4 rounded-2xl border border-border bg-surface p-4">
      <ProjectPicker />
      <DateRangePicker range={range} onPresetChange={onPresetChange} onCustomChange={onCustomChange} />
    </div>
  )
}
