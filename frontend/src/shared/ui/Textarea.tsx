import { cn } from '../utils/cn'
import type { TextareaHTMLAttributes } from 'react'

interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  label?: string
  error?: string
}

export function Textarea({ label, error, className, ...props }: TextareaProps) {
  return (
    <div className="flex flex-col gap-1">
      {label && <label className="text-xs font-medium text-text-muted">{label}</label>}
      <textarea
        className={cn(
          'bg-bg-tertiary border rounded-lg px-3 py-2 text-sm text-text-primary placeholder:text-text-dimmed focus:outline-none focus:border-accent-blue transition-colors resize-y min-h-20',
          error ? 'border-red-500/50' : 'border-border-secondary',
          className
        )}
        {...props}
      />
      {error && <span className="text-xs text-red-400">{error}</span>}
    </div>
  )
}
