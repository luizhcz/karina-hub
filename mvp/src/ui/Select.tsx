import { forwardRef, useId } from 'react'
import { cn } from './cn'
import { ChevronDownIcon } from './Icons'

export interface SelectOption {
  value: string
  label: string
  disabled?: boolean
}

interface SelectProps extends Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'children'> {
  label?: string
  hint?: string
  error?: string
  options: SelectOption[]
  placeholder?: string
}

export const Select = forwardRef<HTMLSelectElement, SelectProps>(function Select(
  { label, hint, error, options, placeholder, className, id, disabled, ...rest },
  ref,
) {
  const generatedId = useId()
  const inputId = id ?? generatedId

  return (
    <div className="flex flex-col gap-1">
      {label && (
        <label htmlFor={inputId} className="text-xs font-medium text-fg-muted">
          {label}
        </label>
      )}
      <div className="relative">
        <select
          ref={ref}
          id={inputId}
          disabled={disabled}
          className={cn(
            'h-9 w-full appearance-none rounded-lg border bg-surface px-3 pr-9 text-sm text-fg disabled:opacity-50',
            'focus:outline-none focus:ring-2 focus:ring-accent/30',
            error ? 'border-danger/60' : 'border-border focus:border-accent',
            className,
          )}
          aria-invalid={!!error || undefined}
          {...rest}
        >
          {placeholder !== undefined && <option value="">{placeholder}</option>}
          {options.map((opt) => (
            <option key={opt.value} value={opt.value} disabled={opt.disabled}>
              {opt.label}
            </option>
          ))}
        </select>
        <ChevronDownIcon className="pointer-events-none absolute right-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-fg-dim" />
      </div>
      {error ? (
        <span className="text-[11px] text-danger">{error}</span>
      ) : hint ? (
        <span className="text-[11px] text-fg-dim">{hint}</span>
      ) : null}
    </div>
  )
})
