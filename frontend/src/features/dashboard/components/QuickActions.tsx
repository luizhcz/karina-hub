import { Link } from 'react-router'
import { Button } from '../../../shared/ui/Button'

export function QuickActions() {
  return (
    <div className="flex flex-wrap gap-3">
      <Link to="/agents/new">
        <Button variant="primary" size="sm">
          New Agent
        </Button>
      </Link>
      <Link to="/chat">
        <Button variant="secondary" size="sm">
          Open Chat
        </Button>
      </Link>
      <Link to="/hitl">
        <Button variant="secondary" size="sm">
          Review HITL
        </Button>
      </Link>
    </div>
  )
}
