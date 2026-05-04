import { forwardRef, useId } from 'react'
import { cn } from './cn'

interface TextareaProps extends React.TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string
  hint?: string
  error?: string
  monospace?: boolean
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { label, hint, error, monospace, className, id, ...rest },
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
      <textarea
        ref={ref}
        id={inputId}
        className={cn(
          'min-h-[60px] w-full resize-y rounded-lg border bg-surface px-3 py-2 text-sm text-fg placeholder:text-fg-dim',
          'focus:outline-none focus:ring-2 focus:ring-accent/30',
          error ? 'border-danger/60' : 'border-border focus:border-accent',
          monospace && 'font-mono text-[12px]',
          className,
        )}
        aria-invalid={!!error || undefined}
        {...rest}
      />
      {error ? (
        <span className="text-[11px] text-danger">{error}</span>
      ) : hint ? (
        <span className="text-[11px] text-fg-dim">{hint}</span>
      ) : null}
    </div>
  )
})
