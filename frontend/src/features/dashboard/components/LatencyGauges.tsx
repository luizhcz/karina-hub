import type { ExecutionSummary } from '../../../api/analytics'
import { GaugeChart } from '../../../shared/charts/GaugeChart'
import { LoadingSpinner } from '../../../shared/ui/LoadingSpinner'
import { LATENCY_GAUGE } from '../../../constants/gauges'

interface Props {
  data: ExecutionSummary | undefined
  isLoading: boolean
}

export function LatencyGauges({ data, isLoading }: Props) {
  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-full">
        <LoadingSpinner />
      </div>
    )
  }

  if (!data) return null

  return (
    <div className="flex flex-col gap-4">
      <GaugeChart
        value={Math.round(data.p50Ms)}
        max={LATENCY_GAUGE.maxMs}
        label="P50 Latency"
        unit="ms"
        thresholds={LATENCY_GAUGE.thresholds}
      />
      <GaugeChart
        value={Math.round(data.p95Ms)}
        max={LATENCY_GAUGE.maxMs}
        label="P95 Latency"
        unit="ms"
        thresholds={LATENCY_GAUGE.thresholds}
      />
    </div>
  )
}
