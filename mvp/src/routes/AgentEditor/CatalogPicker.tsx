import { Badge, ErrorMessage, Spinner, cn } from '../../ui'

export interface CatalogItem {
  id: string
  primary: string
  secondary?: string | null
  badge?: string | null
}

interface CatalogPickerProps {
  loading: boolean
  error: string | null
  items: CatalogItem[]
  selectedIds: string[]
  onToggle: (id: string) => void
  emptyHint: string
  disabled?: boolean
}

// Lista de seleção múltipla com checkbox por linha. Mantém os selecionados no
// topo pra facilitar review quando o catálogo é grande.
export function CatalogPicker({
  loading,
  error,
  items,
  selectedIds,
  onToggle,
  emptyHint,
  disabled,
}: CatalogPickerProps) {
  if (loading) {
    return (
      <div className="flex items-center justify-center py-8">
        <Spinner className="h-5 w-5 text-fg-muted" />
      </div>
    )
  }
  if (error) {
    return <ErrorMessage message={error} />
  }
  if (items.length === 0) {
    return (
      <div className="rounded-lg border border-dashed border-border px-4 py-6 text-center text-xs text-fg-muted">
        {emptyHint}
      </div>
    )
  }

  const selectedSet = new Set(selectedIds)
  const sorted = [...items].sort((a, b) => {
    const aSel = selectedSet.has(a.id) ? 0 : 1
    const bSel = selectedSet.has(b.id) ? 0 : 1
    if (aSel !== bSel) return aSel - bSel
    return a.primary.localeCompare(b.primary)
  })

  return (
    <ul className="divide-y divide-border overflow-hidden rounded-lg border border-border">
      {sorted.map((item) => {
        const checked = selectedSet.has(item.id)
        return (
          <li key={item.id}>
            <label
              className={cn(
                'flex cursor-pointer items-start gap-3 px-4 py-3 transition',
                checked ? 'bg-accent-subtle/40' : 'hover:bg-surface-hover',
                disabled && 'cursor-not-allowed opacity-60',
              )}
            >
              <input
                type="checkbox"
                className="mt-0.5 h-4 w-4 accent-accent"
                checked={checked}
                disabled={disabled}
                onChange={() => onToggle(item.id)}
              />
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <span className="truncate text-sm font-medium text-fg">{item.primary}</span>
                  {item.badge && (
                    <Badge tone="neutral" className="font-mono">
                      {item.badge}
                    </Badge>
                  )}
                </div>
                {item.secondary && (
                  <p className="mt-0.5 line-clamp-2 text-xs text-fg-muted">{item.secondary}</p>
                )}
              </div>
            </label>
          </li>
        )
      })}
    </ul>
  )
}
