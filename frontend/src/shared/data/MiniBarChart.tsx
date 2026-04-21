import { cn } from '../utils/cn'

interface MiniBarChartProps {
  data: number[]
  max?: number
  color?: string
  height?: number
  className?: string
}

export function MiniBarChart({ data, max: maxProp, color = 'bg-accent-blue', height = 32, className }: MiniBarChartProps) {
  const max = maxProp ?? Math.max(...data, 1)

  return (
    <div className={cn('flex items-end gap-px', className)} style={{ height }}>
      {data.map((v, i) => (
        <div
          key={i}
          className={cn('flex-1 rounded-t-sm min-w-[2px]', color)}
          style={{ height: `${Math.max((v / max) * 100, 2)}%` }}
        />
      ))}
    </div>
  )
}
