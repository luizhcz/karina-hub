import { Badge } from '../../../shared/ui/Badge'

export function AgentIndicator({ agentName }: { agentName: string }) {
  return (
    <div className="flex justify-center my-1">
      <Badge variant="purple" pulse>
        Agent: {agentName}
      </Badge>
    </div>
  )
}
