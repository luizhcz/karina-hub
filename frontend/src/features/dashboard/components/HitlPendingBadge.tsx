import { Link } from 'react-router'
import type { HumanInteraction } from '../../../api/interactions'
import { Badge } from '../../../shared/ui/Badge'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'

interface Props {
  data: HumanInteraction[] | undefined
  isLoading: boolean
}

export function HitlPendingBadge({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  const count = data?.length ?? 0

  return (
    <div className="flex flex-col items-center justify-center gap-3 h-full">
      <span className="text-xs text-text-muted">HITL Pending</span>
      <Badge variant={count > 0 ? 'red' : 'green'} pulse={count > 0}>
        {count} pending
      </Badge>
      <Link to="/hitl" className="text-xs text-accent-blue hover:underline">
        Review interactions
      </Link>
    </div>
  )
}
