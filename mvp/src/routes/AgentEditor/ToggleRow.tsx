import { cn } from '../../ui'

interface ToggleRowProps {
  checked: boolean
  disabled: boolean
  onChange: (next: boolean) => void
  label: string
  hint?: string
}

export function ToggleRow({ checked, disabled, onChange, label, hint }: ToggleRowProps) {
  return (
    <div className="flex items-start justify-between gap-4">
      <div className="min-w-0 flex-1">
        <div className="text-sm font-medium text-fg">{label}</div>
        {hint && <p className="mt-1 text-xs text-fg-muted">{hint}</p>}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={cn(
          'relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
          disabled && 'cursor-not-allowed opacity-60',
          checked ? 'bg-accent' : 'bg-bg-soft',
        )}
      >
        <span
          className={cn(
            'inline-block h-4 w-4 transform rounded-full bg-white shadow transition',
            checked ? 'translate-x-6' : 'translate-x-1',
          )}
        />
      </button>
    </div>
  )
}
