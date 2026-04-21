import { useState, useCallback, useRef, useEffect } from 'react'
import { tokenApi, analyticsApi } from '../../api'
import { usePolledData } from '../../hooks/usePolledData'
import { TimeRangeSelector, getFromDate } from './shared/TimeRangeSelector'
import { AlertBar } from './AlertBar'
import { OperationalDashboard } from './OperationalDashboard'
import { AgentPerformanceDashboard } from './AgentPerformanceDashboard'
import { ToolCallsDashboard } from './ToolCallsDashboard'
import { UserExperienceDashboard } from './UserExperienceDashboard'
import { TroubleshootingDashboard } from './TroubleshootingDashboard'
import { LlmOutputsExplorer } from './LlmOutputsExplorer'
import { TestCasesPanel } from './TestCasesPanel'
import { useAlertEngine } from '../../hooks/useAlertEngine'
import type { MonitorTab, TimeRange, WorkflowDef, WorkflowExecution, NodeRecord, ExecutionEventRecord, MonitorAlert, ExecutionSummary, ExecutionTimeseries } from '../../types'
import type { ExecStats } from '../ExecutionsMonitor'
import type { TestCase } from './TestCasesPanel'

export type { ExecutionSummary, ExecutionTimeseries }

const TABS: { id: MonitorTab; label: string }[] = [
  { id: 'operational',     label: 'Operacional' },
  { id: 'agents',          label: 'Agentes' },
  { id: 'tools',           label: 'Tool Calls' },
  { id: 'ux',              label: 'Experiencia' },
  { id: 'troubleshooting', label: 'Troubleshooting' },
  { id: 'outputs',         label: 'Outputs' },
  { id: 'testcases',       label: 'Test Cases' },
]

interface Props {
  workflows: WorkflowDef[]
  execStats: ExecStats | null
  selectedExec: WorkflowExecution | null
  nodeStates: Record<string, NodeRecord>
  events: { type: string; payload: unknown; ts: number }[]
  auditEvents: ExecutionEventRecord[]
  onSelectExec: (exec: WorkflowExecution) => void
}

export function MonitorDashboard({
  workflows, execStats, selectedExec, nodeStates, events, auditEvents, onSelectExec,
}: Props) {
  const [activeTab, setActiveTab] = useState<MonitorTab>('operational')
  const [timeRange, setTimeRange] = useState<TimeRange>('24h')
  const [workflowSummaries, setWorkflowSummaries] = useState<Record<string, ExecutionSummary>>({})
  const [testCases, setTestCases] = useState<TestCase[]>([])

  const handleAddTestCase = (tc: TestCase) => {
    setTestCases(prev => [tc, ...prev])
    setActiveTab('testcases')
  }

  const handleDeleteTestCase = (id: string) => {
    setTestCases(prev => prev.filter(tc => tc.id !== id))
  }

  // Auto-switch to Troubleshooting when a different execution is selected from the left panel
  const prevExecIdRef = useRef(selectedExec?.executionId)
  useEffect(() => {
    if (selectedExec?.executionId && selectedExec.executionId !== prevExecIdRef.current) {
      prevExecIdRef.current = selectedExec.executionId
      setActiveTab('troubleshooting')
    }
  }, [selectedExec?.executionId])

  // Use ref for timeRange so fetchers always read latest value
  // without causing dep changes on every render
  const timeRangeRef = useRef(timeRange)
  timeRangeRef.current = timeRange

  const { data: tokenSummary } = usePolledData(
    useCallback(() => tokenApi.getSummary(getFromDate(timeRangeRef.current)), []),
    10_000,
    [timeRange] // only re-init interval when timeRange actually changes
  )

  const { data: throughput } = usePolledData(
    useCallback(() => tokenApi.getThroughput(getFromDate(timeRangeRef.current)), []),
    15_000,
    [timeRange]
  )

  const { data: analyticsSummary } = usePolledData(
    useCallback(() => analyticsApi.getSummary(getFromDate(timeRangeRef.current)), []),
    30_000,
    [timeRange]
  )

  const { data: analyticsTimeseries } = usePolledData(
    useCallback(() => analyticsApi.getTimeseries(
      getFromDate(timeRangeRef.current),
      undefined,
      undefined,
      timeRangeRef.current === '7d' || timeRangeRef.current === '30d' ? 'day' : 'hour'
    ), []),
    30_000,
    [timeRange]
  )

  // Fetch per-workflow summaries
  useEffect(() => {
    if (!workflows.length) return
    const from = getFromDate(timeRange)
    Promise.all(
      workflows.map(wf =>
        analyticsApi.getSummary(from, undefined, wf.id)
          .then(s => ({ id: wf.id, summary: s }))
          .catch(() => null)
      )
    ).then(results => {
      const map: Record<string, ExecutionSummary> = {}
      for (const r of results) {
        if (r) map[r.id] = r.summary
      }
      setWorkflowSummaries(map)
    })
  }, [workflows, timeRange])

  // Pass events ref to alert engine to avoid re-running on every SSE event
  const eventsRef = useRef(events)
  eventsRef.current = events
  const alerts = useAlertEngine({ execStats, tokenSummary, throughput, eventsRef })

  const handleAlertClick = useCallback((alert: MonitorAlert) => {
    if (alert.id.includes('tool')) setActiveTab('tools')
    else if (alert.id.includes('queue') || alert.id.includes('failure') || alert.id.includes('latency')) setActiveTab('operational')
    else if (alert.id.includes('conversation')) setActiveTab('ux')
    else if (alert.id.includes('token_budget')) setActiveTab('agents')
  }, [])

  return (
    <div className="flex-1 flex flex-col overflow-hidden bg-[#04091A]">
      {/* Alert bar */}
      <AlertBar alerts={alerts} onAlertClick={handleAlertClick} />

      {/* Tab bar + time range */}
      <div className="flex items-center justify-between px-4 py-2 border-b border-[#0C1D38] shrink-0 bg-[#04091A]">
        <div className="flex gap-1">
          {TABS.map(tab => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
                activeTab === tab.id
                  ? 'bg-[#0C1D38] text-[#DCE8F5] border border-[#254980]'
                  : 'text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#081529]'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </div>
        <TimeRangeSelector value={timeRange} onChange={setTimeRange} />
      </div>

      {/* Tab content */}
      <div className="flex-1 overflow-y-auto">
        {activeTab === 'operational' && (
          <OperationalDashboard
            execStats={execStats}
            tokenSummary={tokenSummary}
            throughput={throughput}
            timeRange={timeRange}
            analyticsSummary={analyticsSummary ?? null}
            analyticsTimeseries={analyticsTimeseries ?? null}
            workflows={workflows}
            workflowSummaries={workflowSummaries}
          />
        )}
        {activeTab === 'agents' && (
          <AgentPerformanceDashboard
            tokenSummary={tokenSummary}
            timeRange={timeRange}
          />
        )}
        {activeTab === 'tools' && (
          <ToolCallsDashboard
            workflows={workflows}
            timeRange={timeRange}
          />
        )}
        {activeTab === 'ux' && (
          <UserExperienceDashboard
            workflows={workflows}
            timeRange={timeRange}
          />
        )}
        {activeTab === 'troubleshooting' && (
          <TroubleshootingDashboard
            selectedExec={selectedExec}
            nodeStates={nodeStates}
            events={events}
            auditEvents={auditEvents}
            onSelectExec={onSelectExec}
            workflows={workflows}
            onAddTestCase={handleAddTestCase}
          />
        )}
        {activeTab === 'outputs' && (
          <LlmOutputsExplorer />
        )}
        {activeTab === 'testcases' && (
          <TestCasesPanel
            workflows={workflows}
            testCases={testCases}
            onDelete={handleDeleteTestCase}
          />
        )}
      </div>
    </div>
  )
}
