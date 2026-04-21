import type { ExecutionSummary } from '../../../api/analytics'
import { MetricCard } from '../../../shared/data/MetricCard'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { formatNumber, formatPercent } from '../../../shared/utils/formatters'

interface Props {
  data: ExecutionSummary | undefined
  isLoading: boolean
}

export function ExecutionStats({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  return (
    <div className="grid grid-cols-2 gap-3">
      <MetricCard label="Total" value={formatNumber(data.total)} />
      <MetricCard
        label="Completed"
        value={formatNumber(data.completed)}
        sub={formatPercent(data.successRate)}
        trend="up"
      />
      <MetricCard
        label="Failed"
        value={formatNumber(data.failed)}
        alert={data.failed > 0}
      />
      <MetricCard label="Cancelled" value={formatNumber(data.cancelled)} />
    </div>
  )
}
