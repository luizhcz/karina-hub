import { useEffect, useState } from 'react'
import { friendlyError } from '../api/client'
import { listRouterQuickActions, type RouterQuickAction } from '../api/routerQuickActions'
import { cn } from '../ui/cn'

interface QuickActionsBarProps {
  routerId: string | null
  disabled?: boolean
  /**
   * Click handler:
   * - `hasWildcard=true`: pré-popular input com `displayText + ' '` e focar (não envia).
   * - `hasWildcard=false`: enviar imediatamente.
   */
  onSelect: (action: RouterQuickAction) => void
}

/**
 * Barra de atalhos do Router carregados via GET /agents/{routerId}/quick-actions.
 * Renderiza um chip por entry. Não exibe nada quando o Router não tem atalhos
 * cadastrados (silenciosamente colapsa).
 */
export function QuickActionsBar({ routerId, disabled, onSelect }: QuickActionsBarProps) {
  const [actions, setActions] = useState<RouterQuickAction[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!routerId) {
      setActions([])
      return
    }
    let cancelled = false
    setLoading(true)
    setError(null)
    listRouterQuickActions(routerId)
      .then((data) => {
        if (!cancelled) setActions(data)
      })
      .catch((e) => {
        if (!cancelled) setError(friendlyError(e, 'Falha ao carregar atalhos do Router.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [routerId])

  if (!routerId || (!loading && actions.length === 0 && !error)) return null

  return (
    <div className="flex flex-wrap items-center gap-1.5 px-1 py-1.5" title={error ?? undefined}>
      {loading && <span className="text-[11px] text-fg-dim">Carregando atalhos…</span>}
      {actions.map((a) => (
        <button
          key={a.id}
          type="button"
          disabled={disabled}
          onClick={() => onSelect(a)}
          title={a.description ?? a.pattern}
          className={cn(
            'rounded-full border border-border bg-bg-soft px-3 py-1 text-xs text-fg',
            'transition-colors hover:bg-accent/10 hover:border-accent disabled:cursor-not-allowed disabled:opacity-50',
          )}
        >
          {a.displayText}
          {a.hasWildcard && <span className="ml-1 text-fg-dim">…</span>}
        </button>
      ))}
    </div>
  )
}
