import { Link } from 'react-router'
import type { ExecutionPage } from '../../../api/executions'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { StatusPill } from '../../../shared/data/StatusPill'
import { truncate, formatRelativeTime } from '../../../shared/utils/formatters'

interface Props {
  data: ExecutionPage | undefined
  isLoading: boolean
}

export function RecentErrors({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-32">
        <LoadingSpinner />
      </div>
    )
  }

  const items = data?.items ?? []

  if (items.length === 0) {
    return (
      <p className="text-sm text-text-muted py-6 text-center">
        No recent errors
      </p>
    )
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-xs">
        <thead>
          <tr className="text-text-muted border-b border-border-primary">
            <th className="text-left py-2 px-2 font-medium">Execution</th>
            <th className="text-left py-2 px-2 font-medium">Workflow</th>
            <th className="text-left py-2 px-2 font-medium">Error</th>
            <th className="text-left py-2 px-2 font-medium">Status</th>
            <th className="text-right py-2 px-2 font-medium">Time</th>
          </tr>
        </thead>
        <tbody>
          {items.map((e) => (
            <tr
              key={e.executionId}
              className="border-b border-border-primary hover:bg-bg-tertiary transition-colors"
            >
              <td className="py-2 px-2">
                <Link
                  to={`/executions/${e.executionId}`}
                  className="text-accent-blue hover:underline"
                >
                  {truncate(e.executionId, 12)}
                </Link>
              </td>
              <td className="py-2 px-2 text-text-secondary">
                {truncate(e.workflowId, 16)}
              </td>
              <td className="py-2 px-2 text-red-400">
                {truncate(e.errorMessage ?? 'Unknown error', 40)}
              </td>
              <td className="py-2 px-2">
                <StatusPill status={e.status} />
              </td>
              <td className="py-2 px-2 text-right text-text-muted">
                {formatRelativeTime(e.startedAt)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
