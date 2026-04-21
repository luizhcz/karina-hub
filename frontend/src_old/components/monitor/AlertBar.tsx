import { useState } from 'react'
import type { MonitorAlert } from '../../types'

interface Props {
  alerts: MonitorAlert[]
  onAlertClick?: (alert: MonitorAlert) => void
}

export function AlertBar({ alerts, onAlertClick }: Props) {
  const [dismissed, setDismissed] = useState<Set<string>>(new Set())

  if (alerts.length === 0) return null

  const visible = alerts.filter(a => !dismissed.has(a.id + ':' + a.triggeredAt))
  if (visible.length === 0) return null

  return (
    <div className="shrink-0 border-b border-[#0C1D38] bg-[#04091A] px-4 py-2 space-y-1.5">
      {visible.map(alert => {
        const isCrit = alert.severity === 'CRITICAL'
        return (
          <div
            key={alert.id}
            className={`flex items-center gap-3 px-3 py-2 rounded-lg border ${
              isCrit
                ? 'bg-red-500/10 border-red-500/30'
                : 'bg-amber-500/10 border-amber-500/30'
            } ${isCrit ? 'animate-pulse' : ''}`}
          >
            <span className={`text-sm shrink-0 ${isCrit ? 'text-red-400' : 'text-amber-400'}`}>
              {isCrit ? '\u26A0' : '\u26A1'}
            </span>
            <button
              className="flex-1 text-left min-w-0"
              onClick={() => onAlertClick?.(alert)}
            >
              <div className="flex items-baseline gap-2">
                <span className={`text-[10px] font-bold uppercase ${isCrit ? 'text-red-400' : 'text-amber-400'}`}>
                  {alert.severity}
                </span>
                <span className={`text-xs font-medium ${isCrit ? 'text-red-300' : 'text-amber-300'}`}>
                  {alert.title}
                </span>
              </div>
              <p className="text-[11px] text-[#7596B8] truncate">{alert.message}</p>
            </button>
            <button
              onClick={(e) => {
                e.stopPropagation()
                setDismissed(prev => new Set(prev).add(alert.id + ':' + alert.triggeredAt))
              }}
              className="text-[#3E5F7D] hover:text-[#7596B8] transition-colors shrink-0 p-1"
              title="Dismiss"
            >
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M18 6L6 18M6 6l12 12" />
              </svg>
            </button>
          </div>
        )
      })}
    </div>
  )
}
