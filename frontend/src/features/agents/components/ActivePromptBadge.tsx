import { Badge } from '../../../shared/ui/Badge'
import { useActivePrompt } from '../../../api/prompts'

interface ActivePromptBadgeProps {
  agentId: string
}

export function ActivePromptBadge({ agentId }: ActivePromptBadgeProps) {
  const { data: active, isLoading } = useActivePrompt(agentId)

  if (isLoading) return <Badge variant="gray">...</Badge>

  if (!active?.versionId) return <Badge variant="gray">&mdash;</Badge>

  return <Badge variant="purple">{active.versionId}</Badge>
}
