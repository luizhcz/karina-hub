import { cn } from '../utils/cn'

type Status = 'Running' | 'Completed' | 'Failed' | 'Paused' | 'Cancelled' | 'Pending' | 'running' | 'completed' | 'failed' | 'paused' | 'cancelled' | 'pending'

const colors: Record<string, string> = {
  running: 'bg-status-running/20 text-status-running border-status-running/30',
  completed: 'bg-status-completed/20 text-status-completed border-status-completed/30',
  failed: 'bg-status-failed/20 text-status-failed border-status-failed/30',
  paused: 'bg-status-paused/20 text-status-paused border-status-paused/30',
  cancelled: 'bg-status-cancelled/20 text-status-cancelled border-status-cancelled/30',
  pending: 'bg-status-pending/20 text-status-pending border-status-pending/30',
}

interface StatusPillProps {
  status: Status
  className?: string
}

export function StatusPill({ status, className }: StatusPillProps) {
  const key = status.toLowerCase()
  return (
    <span className={cn(
      'inline-flex items-center gap-1 px-2 py-0.5 text-xs font-medium rounded-full border',
      colors[key] ?? colors['pending'],
      className
    )}>
      <span className={cn('w-1.5 h-1.5 rounded-full', key === 'running' && 'animate-pulse',
        key === 'running' ? 'bg-status-running' :
        key === 'completed' ? 'bg-status-completed' :
        key === 'failed' ? 'bg-status-failed' :
        key === 'paused' ? 'bg-status-paused' :
        key === 'cancelled' ? 'bg-status-cancelled' :
        'bg-status-pending'
      )} />
      {status}
    </span>
  )
}
