import { Badge } from '../../../shared/ui/Badge'
import type { TriggerSource } from '../../../api/evaluations'

interface Props {
  source: TriggerSource
}

const labels: Record<TriggerSource, string> = {
  Manual: 'Manual',
  AgentVersionPublished: 'Auto',
  ApiClient: 'API',
}

const variants: Record<TriggerSource, 'blue' | 'purple' | 'gray'> = {
  Manual: 'blue',
  AgentVersionPublished: 'purple',
  ApiClient: 'gray',
}

export function TriggerSourceBadge({ source }: Props) {
  return <Badge variant={variants[source]}>{labels[source]}</Badge>
}
