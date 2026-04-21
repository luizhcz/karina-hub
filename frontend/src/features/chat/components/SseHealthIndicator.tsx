import { cn } from '../../../shared/utils/cn'

export type SseStatus = 'idle' | 'streaming' | 'error'

interface SseHealthIndicatorProps {
  status: SseStatus
}

const STATUS_CONFIG: Record<SseStatus, { label: string; color: string; pulse: boolean }> = {
  idle: { label: 'Idle', color: 'bg-text-dimmed', pulse: false },
  streaming: { label: 'Streaming', color: 'bg-green-400', pulse: true },
  error: { label: 'Erro', color: 'bg-red-400', pulse: false },
}

export function SseHealthIndicator({ status }: SseHealthIndicatorProps) {
  const config = STATUS_CONFIG[status]

  return (
    <div className="flex items-center gap-1.5 text-[10px] text-text-muted">
      <span className="relative flex h-2 w-2">
        {config.pulse && (
          <span
            className={cn(
              'absolute inline-flex h-full w-full rounded-full opacity-75 animate-ping',
              config.color,
            )}
          />
        )}
        <span
          className={cn('relative inline-flex rounded-full h-2 w-2', config.color)}
        />
      </span>
      <span>SSE: {config.label}</span>
    </div>
  )
}
