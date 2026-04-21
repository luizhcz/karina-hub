import type { ReactNode } from 'react'

interface EmptyStateProps {
  icon?: string
  title: string
  description?: string
  action?: ReactNode
}

export function EmptyState({ icon = '📭', title, description, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center justify-center py-16 gap-3">
      <span className="text-4xl">{icon}</span>
      <h3 className="text-lg font-medium text-text-primary">{title}</h3>
      {description && <p className="text-sm text-text-muted max-w-md text-center">{description}</p>}
      {action && <div className="mt-2">{action}</div>}
    </div>
  )
}
