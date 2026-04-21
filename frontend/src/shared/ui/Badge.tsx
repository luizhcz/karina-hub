import { cn } from '../utils/cn'

type BadgeVariant = 'default' | 'blue' | 'green' | 'red' | 'yellow' | 'purple' | 'gray'

interface BadgeProps {
  children: React.ReactNode
  variant?: BadgeVariant
  className?: string
  pulse?: boolean
}

const variants: Record<BadgeVariant, string> = {
  default: 'bg-bg-tertiary text-text-secondary border-border-secondary',
  blue: 'bg-blue-500/15 text-blue-400 border-blue-500/30',
  green: 'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
  red: 'bg-red-500/15 text-red-400 border-red-500/30',
  yellow: 'bg-amber-500/15 text-amber-400 border-amber-500/30',
  purple: 'bg-purple-500/15 text-purple-400 border-purple-500/30',
  gray: 'bg-gray-500/15 text-gray-400 border-gray-500/30',
}

export function Badge({ children, variant = 'default', className, pulse }: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full border',
        variants[variant],
        pulse && 'animate-pulse',
        className
      )}
    >
      {children}
    </span>
  )
}
