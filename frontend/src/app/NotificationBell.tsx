import { useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { useAgentBreakingChanges, type AgentBreakingChangeNotification } from '../api/notifications'

/**
 * Bell de notification no Header. Mostra count de breaking changes recentes
 * (últimos 7 dias) que afetam agents visíveis ao projeto atual. Click abre
 * dropdown com lista; entry click navega pra página de versions do agent.
 */
export function NotificationBell() {
  const { data, isError } = useAgentBreakingChanges(7)
  const [open, setOpen] = useState(false)
  const dropdownRef = useRef<HTMLDivElement>(null)
  const navigate = useNavigate()

  useEffect(() => {
    if (!open) return
    const handler = (e: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [open])

  const items = data ?? []
  const count = items.length

  if (isError) return null // silencioso — não bloqueia header em caso de erro de API.

  return (
    <div ref={dropdownRef} className="relative">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-label={`${count} notificações de breaking change`}
        className="relative h-8 w-8 flex items-center justify-center rounded-md border border-border-secondary hover:border-accent-blue text-text-muted hover:text-text-primary transition-colors"
      >
        <BellIcon />
        {count > 0 && (
          <span className="absolute -top-1.5 -right-1.5 min-w-[18px] h-[18px] px-1 rounded-full bg-yellow-500 text-bg-primary text-[10px] font-bold flex items-center justify-center">
            {count > 9 ? '9+' : count}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute right-0 top-10 w-96 max-h-[500px] overflow-y-auto bg-bg-secondary border border-border-primary rounded-lg shadow-2xl z-50">
          <div className="px-4 py-3 border-b border-border-primary sticky top-0 bg-bg-secondary">
            <h3 className="text-sm font-semibold text-text-primary">
              Breaking changes recentes
            </h3>
            <p className="text-xs text-text-muted mt-0.5">
              Versions com intent breaking publicadas nos últimos 7 dias.
            </p>
          </div>
          {count === 0 ? (
            <p className="text-sm text-text-muted px-4 py-6 text-center">
              Nenhum breaking change recente.
            </p>
          ) : (
            <ul className="divide-y divide-border-primary">
              {items.map((item) => (
                <NotificationItem
                  key={item.agentVersionId}
                  item={item}
                  onClick={() => {
                    setOpen(false)
                    navigate(`/agents/${item.agentId}/versions`)
                  }}
                />
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  )
}

function NotificationItem({
  item,
  onClick,
}: {
  item: AgentBreakingChangeNotification
  onClick: () => void
}) {
  return (
    <li>
      <button
        type="button"
        onClick={onClick}
        className="w-full text-left px-4 py-3 hover:bg-bg-tertiary transition-colors"
      >
        <div className="flex items-center justify-between gap-2">
          <span className="text-sm font-medium text-text-primary truncate">
            {item.agentName ?? item.agentId}
          </span>
          <span className="text-xs text-yellow-500 font-medium flex-shrink-0">
            rev {item.revision}
          </span>
        </div>
        {item.changeReason && (
          <p className="text-xs text-text-muted mt-1 line-clamp-2">
            {item.changeReason}
          </p>
        )}
        <p className="text-[11px] text-text-dimmed mt-1">
          {formatRelative(item.createdAt)}
          {item.createdBy && ` · ${item.createdBy}`}
        </p>
      </button>
    </li>
  )
}

function formatRelative(iso: string): string {
  const d = new Date(iso)
  const diffMs = Date.now() - d.getTime()
  const diffMin = Math.floor(diffMs / 60_000)
  if (diffMin < 1) return 'agora'
  if (diffMin < 60) return `${diffMin}min atrás`
  const diffH = Math.floor(diffMin / 60)
  if (diffH < 24) return `${diffH}h atrás`
  const diffD = Math.floor(diffH / 24)
  if (diffD < 7) return `${diffD}d atrás`
  return d.toLocaleDateString('pt-BR')
}

function BellIcon() {
  return (
    <svg
      width="16"
      height="16"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      <path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9" />
      <path d="M10.3 21a1.94 1.94 0 0 0 3.4 0" />
    </svg>
  )
}
