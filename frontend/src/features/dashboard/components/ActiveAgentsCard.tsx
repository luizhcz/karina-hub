import { Link } from 'react-router'
import type { AgentDef } from '../../../api/agents'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'

interface Props {
  data: AgentDef[] | undefined
  isLoading: boolean
}

export function ActiveAgentsCard({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end justify-between">
        <span className="text-xs text-text-muted">Active agents</span>
        <span className="text-2xl font-bold text-text-primary">{data.length}</span>
      </div>
      <ul className="flex flex-col gap-1">
        {data.slice(0, 5).map((a) => (
          <li key={a.id} className="text-xs text-text-secondary truncate">
            {a.name}
          </li>
        ))}
      </ul>
      <Link to="/agents" className="text-xs text-accent-blue hover:underline">
        View all agents
      </Link>
    </div>
  )
}
