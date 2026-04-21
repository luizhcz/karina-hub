import { cn } from '../utils/cn'
import type { ReactNode } from 'react'

interface CardProps {
  title?: string
  actions?: ReactNode
  children: ReactNode
  className?: string
  padding?: boolean
}

export function Card({ title, actions, children, className, padding = true }: CardProps) {
  return (
    <div className={cn('bg-bg-secondary border border-border-primary rounded-xl', className)}>
      {(title || actions) && (
        <div className="flex items-center justify-between px-5 py-3 border-b border-border-primary">
          {title && <h3 className="text-sm font-semibold text-text-primary">{title}</h3>}
          {actions && <div className="flex items-center gap-2">{actions}</div>}
        </div>
      )}
      <div className={cn(padding && 'p-5')}>{children}</div>
    </div>
  )
}
