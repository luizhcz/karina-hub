import { cn } from '../utils/cn'
import type { ReactNode } from 'react'

interface MetricCardProps {
  label: string
  value: string | number
  sub?: string
  icon?: ReactNode
  trend?: 'up' | 'down' | 'neutral'
  alert?: boolean
  className?: string
}

export function MetricCard({ label, value, sub, icon, trend, alert, className }: MetricCardProps) {
  return (
    <div className={cn(
      'bg-bg-secondary border rounded-xl p-4 flex flex-col gap-1',
      alert ? 'border-red-500/40' : 'border-border-primary',
      className
    )}>
      <div className="flex items-center justify-between">
        <span className="text-xs text-text-muted">{label}</span>
        {icon && <span className="text-lg">{icon}</span>}
      </div>
      <div className="flex items-end gap-2">
        <span className={cn('text-2xl font-bold', alert ? 'text-red-400' : 'text-text-primary')}>{value}</span>
        {trend && (
          <span className={cn('text-xs font-medium mb-1',
            trend === 'up' ? 'text-emerald-400' : trend === 'down' ? 'text-red-400' : 'text-text-muted'
          )}>
            {trend === 'up' ? '↑' : trend === 'down' ? '↓' : '→'}
          </span>
        )}
      </div>
      {sub && <span className="text-xs text-text-muted">{sub}</span>}
    </div>
  )
}
