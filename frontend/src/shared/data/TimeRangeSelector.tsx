import { cn } from '../utils/cn'
import type { TimeRange } from '../utils/date'

const ranges: { value: TimeRange; label: string }[] = [
  { value: '1h', label: '1h' },
  { value: '6h', label: '6h' },
  { value: '24h', label: '24h' },
  { value: '7d', label: '7d' },
  { value: '30d', label: '30d' },
]

interface TimeRangeSelectorProps {
  value: TimeRange
  onChange: (v: TimeRange) => void
  className?: string
}

export function TimeRangeSelector({ value, onChange, className }: TimeRangeSelectorProps) {
  return (
    <div className={cn('flex gap-1 bg-bg-tertiary rounded-lg p-0.5', className)}>
      {ranges.map((r) => (
        <button
          key={r.value}
          onClick={() => onChange(r.value)}
          className={cn(
            'px-2.5 py-1 text-xs font-medium rounded-md transition-colors',
            value === r.value
              ? 'bg-accent-blue text-white'
              : 'text-text-muted hover:text-text-secondary'
          )}
        >
          {r.label}
        </button>
      ))}
    </div>
  )
}
