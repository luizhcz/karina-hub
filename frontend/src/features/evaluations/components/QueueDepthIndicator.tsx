import { useQuery } from '@tanstack/react-query'
import { get } from '../../../api/client'
import type { EvaluationRun } from '../../../api/evaluations'
import { Badge } from '../../../shared/ui/Badge'

interface Props {
  agentId: string
}

export function QueueDepthIndicator({ agentId }: Props) {
  const { data } = useQuery<EvaluationRun[]>({
    queryKey: ['agent-runs-pending', agentId],
    queryFn: () => get<EvaluationRun[]>(`/agents/${agentId}/evaluations/runs`, { take: 50 }),
    refetchInterval: 10_000,
    enabled: !!agentId,
  })

  if (!data) return null
  const pending = data.filter((r) => r.status === 'Pending').length
  const running = data.filter((r) => r.status === 'Running').length
  if (pending === 0 && running === 0) return null

  return (
    <div className="flex items-center gap-2 text-xs">
      {running > 0 && <Badge variant="blue" pulse>● Running: {running}</Badge>}
      {pending > 0 && <Badge variant="yellow">⏳ Pending: {pending}</Badge>}
    </div>
  )
}
