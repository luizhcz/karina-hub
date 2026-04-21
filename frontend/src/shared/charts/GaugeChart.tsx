import { cn } from '../utils/cn'

interface GaugeChartProps {
  value: number
  max: number
  label: string
  unit?: string
  thresholds?: { green: number; yellow: number }
  className?: string
}

export function GaugeChart({ value, max, label, unit = '', thresholds, className }: GaugeChartProps) {
  const pct = Math.min((value / max) * 100, 100)
  const color = thresholds
    ? value <= thresholds.green ? 'bg-emerald-500' : value <= thresholds.yellow ? 'bg-amber-500' : 'bg-red-500'
    : 'bg-accent-blue'

  return (
    <div className={cn('flex flex-col gap-2', className)}>
      <div className="flex items-end justify-between">
        <span className="text-xs text-text-muted">{label}</span>
        <span className="text-sm font-semibold text-text-primary">{value}{unit} / {max}{unit}</span>
      </div>
      <div className="h-2 bg-bg-tertiary rounded-full overflow-hidden">
        <div className={cn('h-full rounded-full transition-all duration-500', color)} style={{ width: `${pct}%` }} />
      </div>
    </div>
  )
}
