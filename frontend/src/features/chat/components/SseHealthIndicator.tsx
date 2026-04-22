import { cn } from '../../../shared/utils/cn'

export type SseStatus = 'idle' | 'streaming' | 'reconnecting' | 'error'

interface SseHealthIndicatorProps {
  status: SseStatus
  /** Tentativa atual de reconexão (0 = não reconectando, 1..N = tentativa ativa). */
  reconnectAttempt?: number
}

const STATUS_CONFIG: Record<SseStatus, { label: string; color: string; pulse: boolean }> = {
  idle: { label: 'Idle', color: 'bg-text-dimmed', pulse: false },
  streaming: { label: 'Streaming', color: 'bg-green-400', pulse: true },
  reconnecting: { label: 'Reconectando', color: 'bg-amber-400', pulse: true },
  error: { label: 'Erro', color: 'bg-red-400', pulse: false },
}

export function SseHealthIndicator({ status, reconnectAttempt }: SseHealthIndicatorProps) {
  const config = STATUS_CONFIG[status]
  const label =
    status === 'reconnecting' && reconnectAttempt && reconnectAttempt > 0
      ? `${config.label} (${reconnectAttempt})`
      : config.label

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
      <span>SSE: {label}</span>
    </div>
  )
}
