import { forwardRef, useId } from 'react'
import { cn } from './cn'

interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string
  hint?: string
  error?: string
  /** ícone renderizado dentro do input à esquerda (ex.: lupa em search) */
  leftAddon?: React.ReactNode
  monospace?: boolean
  invalid?: boolean
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { label, hint, error, leftAddon, monospace, invalid, className, id, ...rest },
  ref,
) {
  const generatedId = useId()
  const inputId = id ?? generatedId
  const showError = !!error || !!invalid

  return (
    <div className="flex flex-col gap-1">
      {label && (
        <label htmlFor={inputId} className="text-xs font-medium text-fg-muted">
          {label}
        </label>
      )}
      <div className="relative">
        {leftAddon && (
          <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-fg-dim">
            {leftAddon}
          </span>
        )}
        <input
          ref={ref}
          id={inputId}
          className={cn(
            'h-9 w-full rounded-lg border bg-surface px-3 text-sm text-fg placeholder:text-fg-dim',
            'focus:outline-none focus:ring-2 focus:ring-accent/30',
            showError ? 'border-danger/60' : 'border-border focus:border-accent',
            leftAddon ? 'pl-9' : undefined,
            monospace && 'font-mono text-[12px]',
            className,
          )}
          aria-invalid={showError || undefined}
          {...rest}
        />
      </div>
      {error ? (
        <span className="text-[11px] text-danger">{error}</span>
      ) : hint ? (
        <span className="text-[11px] text-fg-dim">{hint}</span>
      ) : null}
    </div>
  )
})
