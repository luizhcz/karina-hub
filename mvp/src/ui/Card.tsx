import { forwardRef } from 'react'
import { cn } from './cn'

interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** quando false, remove padding interno (útil pra DataTable embutida) */
  padded?: boolean
  /** muda hover state pra indicar que é clicável */
  interactive?: boolean
}

export const Card = forwardRef<HTMLDivElement, CardProps>(function Card(
  { padded = true, interactive = false, className, children, ...rest },
  ref,
) {
  return (
    <div
      ref={ref}
      className={cn(
        'rounded-xl border border-border bg-surface shadow-card',
        padded && 'p-5',
        interactive && 'transition hover:border-accent/40 hover:shadow-soft',
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  )
})

interface CardHeaderProps {
  title: React.ReactNode
  description?: React.ReactNode
  actions?: React.ReactNode
  className?: string
}

export function CardHeader({ title, description, actions, className }: CardHeaderProps) {
  return (
    <div className={cn('flex items-start justify-between gap-3', className)}>
      <div className="min-w-0">
        <h3 className="text-sm font-semibold text-fg">{title}</h3>
        {description && <p className="mt-1 text-xs text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  )
}
