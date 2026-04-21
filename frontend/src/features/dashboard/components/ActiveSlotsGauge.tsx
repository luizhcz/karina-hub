import type { ExecutionSummary } from '../../../api/analytics'
import { GaugeChart } from '../../../shared/charts/GaugeChart'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { ACTIVE_SLOTS_GAUGE } from '../../../constants/gauges'

interface Props {
  data: ExecutionSummary | undefined
  isLoading: boolean
}

export function ActiveSlotsGauge({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs text-text-muted">Active execution slots</span>
      <GaugeChart
        value={data.running}
        max={ACTIVE_SLOTS_GAUGE.maxSlots}
        label="Running"
        thresholds={ACTIVE_SLOTS_GAUGE.thresholds}
      />
    </div>
  )
}
