import type { WorkflowDef } from '../../../api/workflows'
import { Badge } from '../../../shared/ui/Badge'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'

interface Props {
  data: WorkflowDef[] | undefined
  isLoading: boolean
}

const triggerVariant = {
  OnDemand: 'blue',
  Scheduled: 'purple',
  EventDriven: 'yellow',
} as const

type TriggerType = keyof typeof triggerVariant

export function WorkflowsCard({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  const grouped = data.reduce<Record<string, number>>((acc, w) => {
    const type = w.trigger?.type ?? 'OnDemand'
    acc[type] = (acc[type] ?? 0) + 1
    return acc
  }, {})

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end justify-between">
        <span className="text-xs text-text-muted">Workflows</span>
        <span className="text-2xl font-bold text-text-primary">{data.length}</span>
      </div>
      <div className="flex flex-wrap gap-2">
        {Object.entries(grouped).map(([type, count]) => (
          <Badge
            key={type}
            variant={triggerVariant[type as TriggerType] ?? 'default'}
          >
            {type}: {count}
          </Badge>
        ))}
      </div>
    </div>
  )
}
