import { useState } from 'react'
import type { TimeRange } from '../../shared/utils/date'
import { getFromDate } from '../../shared/utils/date'
import { useExecutionSummary, useExecutionTimeseries } from '../../api/analytics'
import { useTokenSummary } from '../../api/token-usage'
import { useAgents } from '../../api/agents'
import { useWorkflows } from '../../api/workflows'
import { usePendingInteractions } from '../../api/interactions'
import { useExecutions } from '../../api/executions'
import { Card } from '../../shared/ui/Card'
import { TimeRangeSelector } from '../../shared/data/TimeRangeSelector'
import { TimeseriesChart } from '../../shared/charts/TimeseriesChart'
import { ExecutionStats } from './components/ExecutionStats'
import { LatencyGauges } from './components/LatencyGauges'
import { CostBudgetCard } from './components/CostBudgetCard'
import { TokensCard } from './components/TokensCard'
import { ActiveAgentsCard } from './components/ActiveAgentsCard'
import { WorkflowsCard } from './components/WorkflowsCard'
import { HitlPendingBadge } from './components/HitlPendingBadge'
import { ActiveSlotsGauge } from './components/ActiveSlotsGauge'
import { QueueDepthCard } from './components/QueueDepthCard'
import { CircuitBreakerStatus } from './components/CircuitBreakerStatus'
import { RecentErrors } from './components/RecentErrors'
import { QuickActions } from './components/QuickActions'

export function DashboardPage() {
  const [range, setRange] = useState<TimeRange>('24h')
  const from = getFromDate(range)

  const summary = useExecutionSummary({ from })
  const timeseries = useExecutionTimeseries({ from })
  const tokens = useTokenSummary({ from })
  const agents = useAgents()
  const workflows = useWorkflows()
  const pending = usePendingInteractions()
  const errors = useExecutions({ status: 'Failed', pageSize: 5 })

  const chartData = (timeseries.data?.buckets ?? []).map((b) => ({
    bucket: b.bucket.split('T')[1]?.slice(0, 5) ?? b.bucket,
    completed: b.completed,
    failed: b.failed,
  }))

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <h1 className="text-xl font-bold text-text-primary">Dashboard</h1>
        <TimeRangeSelector value={range} onChange={setRange} />
      </div>

      {/* Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {/* Row 1 */}
        <Card title="Executions">
          <ExecutionStats data={summary.data} isLoading={summary.isLoading} />
        </Card>
        <Card title="Latency">
          <LatencyGauges data={summary.data} isLoading={summary.isLoading} />
        </Card>
        <Card title="Cost vs Budget">
          <CostBudgetCard data={tokens.data} isLoading={tokens.isLoading} />
        </Card>
        <Card title="Tokens">
          <TokensCard data={tokens.data} isLoading={tokens.isLoading} />
        </Card>

        {/* Row 2 */}
        <Card title="Execution Timeseries" className="md:col-span-2">
          {timeseries.isLoading ? (
            <div className="flex items-center justify-center h-[300px]">
              <span className="text-sm text-text-muted">Loading chart...</span>
            </div>
          ) : chartData.length === 0 ? (
            <div className="flex items-center justify-center h-[300px]">
              <span className="text-sm text-text-muted">No data for this period</span>
            </div>
          ) : (
            <TimeseriesChart
              data={chartData}
              xKey="bucket"
              series={[
                { key: 'completed', label: 'Completed', color: '#34d399' },
                { key: 'failed', label: 'Failed', color: '#f87171' },
              ]}
              stacked
            />
          )}
        </Card>
        <Card title="Agents">
          <ActiveAgentsCard data={agents.data} isLoading={agents.isLoading} />
        </Card>
        <Card title="Workflows">
          <WorkflowsCard data={workflows.data} isLoading={workflows.isLoading} />
        </Card>

        {/* Row 3 */}
        <Card title="HITL">
          <HitlPendingBadge data={pending.data} isLoading={pending.isLoading} />
        </Card>
        <Card title="Active Slots">
          <ActiveSlotsGauge data={summary.data} isLoading={summary.isLoading} />
        </Card>
        <QueueDepthCard />
        <CircuitBreakerStatus />

        {/* Row 4 */}
        <Card title="Recent Errors" className="md:col-span-2">
          <RecentErrors data={errors.data} isLoading={errors.isLoading} />
        </Card>
        <Card title="Quick Actions" className="md:col-span-2">
          <QuickActions />
        </Card>
      </div>
    </div>
  )
}
