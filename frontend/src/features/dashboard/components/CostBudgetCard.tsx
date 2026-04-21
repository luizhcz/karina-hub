import type { GlobalTokenSummary } from '../../../api/token-usage'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { formatUsd } from '../../../shared/utils/formatters'
import { cn } from '../../../shared/utils/cn'

interface Props {
  data: GlobalTokenSummary | undefined
  isLoading: boolean
}

const DAILY_BUDGET = 50

export function CostBudgetCard({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  const estimatedCost = data.totalTokens * 0.000003
  const pct = Math.min((estimatedCost / DAILY_BUDGET) * 100, 100)
  const overBudget = pct > 80

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-end justify-between">
        <span className="text-xs text-text-muted">Cost today</span>
        <span className={cn('text-2xl font-bold', overBudget ? 'text-red-400' : 'text-text-primary')}>
          {formatUsd(estimatedCost)}
        </span>
      </div>
      <div className="h-2 bg-bg-tertiary rounded-full overflow-hidden">
        <div
          className={cn(
            'h-full rounded-full transition-all duration-500',
            overBudget ? 'bg-red-500' : 'bg-emerald-500',
          )}
          style={{ width: `${pct}%` }}
        />
      </div>
      <div className="flex justify-between text-xs text-text-muted">
        <span>Budget: {formatUsd(DAILY_BUDGET)}</span>
        <span>{pct.toFixed(0)}%</span>
      </div>
      {overBudget && (
        <p className="text-xs text-red-400 font-medium">
          Cost exceeds 80% of daily budget
        </p>
      )}
    </div>
  )
}
