import type { GlobalTokenSummary } from '../../../api/token-usage'
import { MetricCard } from '../../../shared/data/MetricCard'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { formatNumber } from '../../../shared/utils/formatters'

interface Props {
  data: GlobalTokenSummary | undefined
  isLoading: boolean
}

export function TokensCard({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  return (
    <MetricCard
      label="Tokens consumed"
      value={formatNumber(data.totalTokens)}
      sub={`${formatNumber(data.totalInput)} in / ${formatNumber(data.totalOutput)} out`}
    />
  )
}
