import { cn } from '../utils/cn'
import type { InputHTMLAttributes } from 'react'

interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  label?: string
  error?: string
}

export function Input({ label, error, className, ...props }: InputProps) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label className="text-xs font-medium text-text-muted">{label}</label>}
      <input
        className={cn(
          'bg-bg-tertiary border rounded-lg px-3 py-2 text-sm text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue transition-colors',
          error ? 'border-red-500/50' : 'border-border-secondary',
          className
        )}
        {...props}
      />
      {error && <span className="text-xs text-red-400">{error}</span>}
    </div>
  )
}
