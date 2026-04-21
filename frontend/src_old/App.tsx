import { useState, useEffect, useCallback, useRef, useMemo } from 'react'
import { api, toolsApi } from './api'
import { useSSE } from './hooks/useSSE'
import { useSSEEventHandler } from './hooks/useSSEEventHandler'
import { WorkflowCanvas } from './components/WorkflowCanvas'
import { ExecutionPanel } from './components/ExecutionPanel'
import { AgentDetailModal } from './components/AgentDetailModal'
import { ChatPanel } from './components/ChatPanel'
import { ExecutionsMonitor } from './components/ExecutionsMonitor'
import type { ExecStats } from './components/ExecutionsMonitor'
import { MonitorDashboard } from './components/monitor/MonitorDashboard'
import { AdminPanel } from './components/AdminPanel'
import { SchedulePanel } from './components/SchedulePanel'
import type { AgentDef, WorkflowDef, WorkflowExecution, NodeRecord, ExecutionEventRecord, ToolInvocation } from './types'

export default function App() {
  const [mode, setMode] = useState<'dashboard' | 'chat' | 'monitor' | 'admin' | 'schedule'>('dashboard')
  const [workflows, setWorkflows] = useState<WorkflowDef[]>([])
  const [selectedWf, setSelectedWf] = useState<WorkflowDef | null>(null)
  const [executions, setExecutions] = useState<WorkflowExecution[]>([])
  const [selectedExec, setSelectedExec] = useState<WorkflowExecution | null>(null)
  const [nodeStates, setNodeStates] = useState<Record<string, NodeRecord>>({})
  const [events, setEvents] = useState<{ type: string; payload: unknown; ts: number }[]>([])
  const [auditEvents, setAuditEvents] = useState<ExecutionEventRecord[]>([])
  const [input, setInput] = useState('Inicie a análise dos ativos financeiros.')
  const [triggering, setTriggering] = useState(false)
  const [activeExecId, setActiveExecId] = useState<string | null>(null)
  const [selectedAgent, setSelectedAgent] = useState<AgentDef | null>(null)
  const [agentLoading, setAgentLoading] = useState(false)
  const [execStats, setExecStats] = useState<ExecStats | null>(null)
  const [chatExecId, setChatExecId] = useState<string | null>(null)
  const [chatWorkflowId, setChatWorkflowId] = useState<string | null>(null)
  const [chatNodeStates, setChatNodeStates] = useState<Record<string, NodeRecord>>({})
  const [chatEvents, setChatEvents] = useState<{ type: string; payload: unknown; ts: number }[]>([])
  const [chatExecution, setChatExecution] = useState<WorkflowExecution | null>(null)
  const [chatAuditEvents, setChatAuditEvents] = useState<ExecutionEventRecord[]>([])
  const [toolInvocations, setToolInvocations] = useState<ToolInvocation[]>([])
  const [chatToolInvocations, setChatToolInvocations] = useState<ToolInvocation[]>([])
  const [chatExecHistory, setChatExecHistory] = useState<string[]>([])
  const [selectedExecHistoryIdx, setSelectedExecHistoryIdx] = useState<number | null>(null)
  const [showChatMonitor, setShowChatMonitor] = useState(true)
  const [showExecList, setShowExecList] = useState(true)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)
  // Refs to avoid stale closures in callbacks without recreating them on every state change
  const chatExecIdRef = useRef<string | null>(null)
  const showChatMonitorRef = useRef(true)

  useEffect(() => {
    api.getWorkflows().then(wfs => {
      setWorkflows(wfs)
      if (wfs.length > 0) setSelectedWf(wfs[0])
    }).catch(console.error)
  }, [])

  useEffect(() => {
    if (!selectedWf) return
    api.getWorkflowExecutions(selectedWf.id)
      .then(execs => {
        setExecutions(execs)
        if (execs.length > 0) setSelectedExec(execs[0])
      })
      .catch(console.error)
  }, [selectedWf])

  useEffect(() => {
    if (!selectedExec) { setNodeStates({}); setAuditEvents([]); setToolInvocations([]); return }
    api.getNodes(selectedExec.executionId)
      .then(nodes => {
        const map: Record<string, NodeRecord> = {}
        nodes.forEach(n => { map[n.nodeId] = n })
        setNodeStates(map)
      })
      .catch(console.error)
    api.getExecutionEvents(selectedExec.executionId)
      .then(setAuditEvents)
      .catch(console.error)
    toolsApi.getByExecution(selectedExec.executionId)
      .then(setToolInvocations)
      .catch(console.error)
  }, [selectedExec?.executionId])

  const handleSSEEvent = useSSEEventHandler({
    execId: activeExecId,
    setEvents,
    setNodeStates,
    setAuditEvents,
    setExecution: useCallback((exec: WorkflowExecution | null) => {
      if (exec) {
        setSelectedExec(exec)
        setExecutions(prev => prev.map(e => e.executionId === exec.executionId ? exec : e))
      }
    }, []),
    onComplete: useCallback(() => {
      if (pollRef.current) clearInterval(pollRef.current)
      setActiveExecId(null)
    }, []),
  })

  useSSE(activeExecId, handleSSEEvent)

  useEffect(() => {
    if (!activeExecId) return
    pollRef.current = setInterval(() => {
      api.getExecution(activeExecId).then(exec => {
        setSelectedExec(exec)
        if (exec.status !== 'Running' && exec.status !== 'Pending') {
          if (pollRef.current) clearInterval(pollRef.current)
          setActiveExecId(null)
        }
      }).catch(console.error)
    }, 3000)
    return () => { if (pollRef.current) clearInterval(pollRef.current) }
  }, [activeExecId])

  const handleNodeClick = useCallback((nodeId: string, nodeType: 'agent' | 'executor') => {
    if (nodeType !== 'agent') return
    setAgentLoading(true)
    setSelectedAgent(null)
    api.getAgent(nodeId)
      .then(setSelectedAgent)
      .catch(console.error)
      .finally(() => setAgentLoading(false))
  }, [])

  // Keep refs in sync with state (no re-render cost, no stale closure)
  chatExecIdRef.current = chatExecId
  showChatMonitorRef.current = showChatMonitor

  // ── Chat execution monitoring ──────────────────────────��───────────────
  // Accumulate events across executions — add separator marker when a new execution starts
  const handleChatExecutionChange = useCallback((info: { executionId: string | null; workflowId: string | null }) => {
    if (info.executionId) {
      setChatExecId(info.executionId)
      setChatWorkflowId(info.workflowId)
      setChatNodeStates({})
      setChatExecHistory(prev => prev.includes(info.executionId!) ? prev : [...prev, info.executionId!])
      setSelectedExecHistoryIdx(null)
      // Separator markers for the audit timeline (lightweight, always)
      const ts = Date.now()
      setChatEvents(prev => [...prev, { type: '__separator__', payload: { executionId: info.executionId }, ts }])
      setChatAuditEvents(prev => [...prev.slice(-200), {
        eventType: '__separator__',
        executionId: info.executionId!,
        payload: JSON.stringify({ executionId: info.executionId }),
        timestamp: new Date().toISOString(),
      }])
      // Workflow Path calls only when monitor panel is open
      if (showChatMonitorRef.current) {
        api.getExecution(info.executionId).then(setChatExecution).catch(console.error)
      }
    } else {
      // Execution completed
      const completedId = chatExecIdRef.current
      setChatExecId(null)
      // Only fetch Workflow Path data if monitor is visible — Chat Path must stay clean
      if (completedId && showChatMonitorRef.current) {
        api.getExecution(completedId).then(setChatExecution).catch(console.error)
        api.getNodes(completedId).then(nodes => {
          const map: Record<string, NodeRecord> = {}
          nodes.forEach(n => { map[n.nodeId] = n })
          setChatNodeStates(map)
        }).catch(console.error)
        setTimeout(() => {
          if (!showChatMonitorRef.current) return // re-check after delay
          api.getExecutionEvents(completedId).then(evts => {
            setChatAuditEvents(prev => [...prev.slice(-200), ...evts])
          }).catch(console.error)
          toolsApi.getByExecution(completedId)
            .then(tools => setChatToolInvocations(prev => {
              const existingIds = new Set(prev.map(t => t.id))
              const fresh = tools.filter(t => !existingIds.has(t.id))
              return fresh.length > 0 ? [...prev, ...fresh] : prev
            }))
            .catch(console.error)
        }, 800)
      }
    }
  }, []) // no deps — uses refs to avoid stale closures

  // Called when user clicks an execution badge in a chat message — load that specific execution
  // without resetting the accumulated audit history or adding separator markers
  const handleSelectChatExecution = useCallback((execId: string) => {
    api.getExecution(execId).then(setChatExecution).catch(console.error)
    api.getNodes(execId).then(nodes => {
      const map: Record<string, NodeRecord> = {}
      nodes.forEach(n => { map[n.nodeId] = n })
      setChatNodeStates(map)
    }).catch(console.error)
    setChatExecHistory(prev => {
      const idx = prev.indexOf(execId)
      if (idx !== -1) {
        setSelectedExecHistoryIdx(idx)
        return prev
      }
      // Exec not yet in history (e.g. loaded from prior conversation) — append it
      const next = [...prev, execId]
      setSelectedExecHistoryIdx(next.length - 1)
      return next
    })
  }, [])

  const handleChatSSEEvent = useSSEEventHandler({
    execId: chatExecId,
    setEvents: setChatEvents,
    setNodeStates: setChatNodeStates,
    setAuditEvents: setChatAuditEvents,
    setExecution: useCallback((exec: WorkflowExecution) => setChatExecution(exec), []),
    maxEvents: 500,
    appendAudit: true,
  })

  // Only open the execution SSE (Workflow Path) when the monitor panel is visible.
  // When hidden, the chat runs on Chat Path only — a single SSE connection per conversation.
  useSSE(showChatMonitor ? chatExecId : null, handleChatSSEEvent)

  // When user reopens the monitor panel, load the latest completed execution data on demand
  useEffect(() => {
    if (!showChatMonitor) return
    const lastId = chatExecHistory[chatExecHistory.length - 1]
    if (!lastId || chatExecId) return // skip if active execution (SSE will handle it)
    api.getExecution(lastId).then(setChatExecution).catch(console.error)
    api.getNodes(lastId).then(nodes => {
      const map: Record<string, NodeRecord> = {}
      nodes.forEach(n => { map[n.nodeId] = n })
      setChatNodeStates(map)
    }).catch(console.error)
  }, [showChatMonitor]) // eslint-disable-line react-hooks/exhaustive-deps

  const chatWorkflow = useMemo(() =>
    workflows.find(w => w.id === chatWorkflowId) ?? null
  , [workflows, chatWorkflowId])

  const handleMonitorSelect = useCallback((exec: WorkflowExecution) => {
    setEvents([])
    setAuditEvents([])
    setToolInvocations([])
    setNodeStates({})
    setSelectedExec(exec)
    api.getNodes(exec.executionId).then(nodes => {
      const map: Record<string, NodeRecord> = {}
      nodes.forEach(n => { map[n.nodeId] = n })
      setNodeStates(map)
    }).catch(console.error)
    api.getExecutionEvents(exec.executionId).then(setAuditEvents).catch(console.error)
    toolsApi.getByExecution(exec.executionId).then(setToolInvocations).catch(console.error)
    // Always connect SSE — Redis event bus replays historical events for completed executions too
    setActiveExecId(exec.executionId)
  }, [])

  const triggerWorkflow = async () => {
    if (!selectedWf || triggering) return
    setTriggering(true)
    setEvents([])
    setAuditEvents([])
    setNodeStates({})
    try {
      const { executionId } = await api.triggerWorkflow(selectedWf.id, input)
      const exec = await api.getExecution(executionId)
      setSelectedExec(exec)
      setExecutions(prev => [exec, ...prev])
      setActiveExecId(executionId)
    } catch (e) {
      console.error(e)
    } finally {
      setTriggering(false)
    }
  }

  return (
    <div className="flex flex-col h-screen bg-[#04091A] text-[#DCE8F5]">
      {/* Top bar */}
      <header className="flex items-center gap-6 px-5 py-3 bg-[#081529]/80 backdrop-blur border-b border-[#0C1D38] shrink-0">
        <div className="flex items-center gap-2.5 shrink-0">
          <div className="w-7 h-7 rounded-lg bg-[#DCE8F5] flex items-center justify-center text-[#04091A] text-xs font-bold">AI</div>
          <span className="text-sm font-semibold text-[#DCE8F5] tracking-tight">EFS AI Hub</span>
        </div>

        <div className="h-5 w-px bg-[#1A3357]" />

        {/* Mode toggle */}
        <div className="flex items-center gap-1 bg-[#0C1D38] rounded-lg p-1">
          <button
            onClick={() => setMode('dashboard')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'dashboard' ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#DCE8F5]'}`}
          >Dashboard</button>
          <button
            onClick={() => setMode('chat')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'chat' ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#DCE8F5]'}`}
          >Chat</button>
          <button
            onClick={() => setMode('monitor')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'monitor' ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#DCE8F5]'}`}
          >Monitor</button>
          <button
            onClick={() => setMode('admin')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'admin' ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#DCE8F5]'}`}
          >Admin</button>
          <button
            onClick={() => setMode('schedule')}
            className={`px-3 py-1 rounded-md text-xs font-medium transition-colors ${mode === 'schedule' ? 'bg-[#DCE8F5] text-[#04091A]' : 'text-[#4A6B8A] hover:text-[#DCE8F5]'}`}
          >Schedule</button>
        </div>

        <div className="h-5 w-px bg-[#1A3357]" />

        {mode === 'dashboard' && <div className="flex items-center gap-3">
          <label className="flex flex-col gap-0.5">
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A] font-medium">Workflow</span>
            <select
              className="bg-[#081529] border border-[#1A3357] rounded-md px-2.5 py-1.5 text-sm text-[#DCE8F5] focus:border-[#0057E0] focus:ring-1 focus:ring-[#0057E030] outline-none transition-colors"
              value={selectedWf?.id ?? ''}
              onChange={e => {
                const wf = workflows.find(w => w.id === e.target.value)
                if (wf) { setSelectedWf(wf); setSelectedExec(null); setNodeStates({}); setEvents([]) }
              }}
            >
              {workflows.map(w => <option key={w.id} value={w.id}>{w.name}</option>)}
            </select>
          </label>

          <label className="flex flex-col gap-0.5">
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A] font-medium">Execution</span>
            <select
              className="bg-[#081529] border border-[#1A3357] rounded-md px-2.5 py-1.5 text-sm text-[#DCE8F5] focus:border-[#0057E0] focus:ring-1 focus:ring-[#0057E030] outline-none transition-colors"
              value={selectedExec?.executionId ?? ''}
              onChange={e => {
                const ex = executions.find(x => x.executionId === e.target.value)
                if (ex) { setSelectedExec(ex); setNodeStates({}); setEvents([]) }
              }}
            >
              <option value="">Selecione...</option>
              {executions.map(ex => (
                <option key={ex.executionId} value={ex.executionId}>
                  {ex.status} · {new Date(ex.startedAt).toLocaleTimeString()}
                </option>
              ))}
            </select>
          </label>
        </div>}

        {mode === 'dashboard' && <div className="h-5 w-px bg-[#1A3357]" />}

        {mode === 'dashboard' && <div className="flex-1 flex items-end gap-2">
          <label className="flex flex-col gap-0.5 flex-1 max-w-md">
            <span className="text-[10px] uppercase tracking-wider text-[#4A6B8A] font-medium">Input</span>
            <input
              className="bg-[#081529] border border-[#1A3357] rounded-md px-2.5 py-1.5 text-sm text-[#DCE8F5] placeholder:text-[#3E5F7D] focus:border-[#0057E0] focus:ring-1 focus:ring-[#0057E030] outline-none transition-colors"
              value={input}
              onChange={e => setInput(e.target.value)}
              placeholder="Input para o workflow..."
            />
          </label>
          <button
            onClick={triggerWorkflow}
            disabled={!selectedWf || triggering || !!activeExecId}
            className="px-4 py-1.5 bg-[#DCE8F5] hover:bg-white text-[#04091A] disabled:opacity-40 disabled:cursor-not-allowed rounded-md text-sm font-medium transition-all active:scale-[0.98]"
          >
            {triggering ? 'Starting...' : activeExecId ? 'Running...' : 'Run'}
          </button>
        </div>}

        {mode === 'dashboard' && selectedWf && (
          <div className="flex items-center gap-2 shrink-0">
            <span className="px-2 py-0.5 rounded-md bg-[#0C1D38] text-[11px] text-[#7596B8] font-medium">{selectedWf.orchestrationMode}</span>
            <span className="text-[11px] text-[#4A6B8A]">{(selectedWf.agents?.length ?? 0)} agents</span>
            <span className="text-[#254980]">·</span>
            <span className="text-[11px] text-[#4A6B8A]">{(selectedWf.executors?.length ?? 0)} executors</span>
          </div>
        )}
      </header>

      {/* Main content */}
      {mode === 'chat' ? (
        <div className="flex flex-1 overflow-hidden">
          {/* Chat panel */}
          <div className={`${showChatMonitor && chatWorkflow ? 'w-[420px]' : 'flex-1 max-w-3xl mx-auto'} border-r border-[#0C1D38] bg-[#04091A] overflow-hidden flex flex-col transition-all`}>
            <ChatPanel
              chatWorkflows={workflows.filter(w => w.configuration?.inputMode === 'Chat')}
              onExecutionChange={handleChatExecutionChange}
              onSelectExecution={handleSelectChatExecution}
            />
          </div>

          {/* Monitor side — workflow diagram + execution panel */}
          {showChatMonitor && chatWorkflow && (
            <div className="flex-1 flex flex-col overflow-hidden relative">
              {/* Toggle + header */}
              <div className="flex items-center gap-3 px-4 py-2 border-b border-[#0C1D38] bg-[#081529] shrink-0">
                <div className="flex items-center gap-2">
                  <span className="w-2 h-2 rounded-full" style={{
                    background: chatExecId ? '#f59e0b' : chatExecution?.status === 'Completed' ? '#10b981' : '#3E5F7D',
                    boxShadow: chatExecId ? '0 0 6px #f59e0b' : 'none',
                    animation: chatExecId ? 'pulse-dot 1.5s ease-in-out infinite' : 'none',
                  }} />
                  <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">
                    {chatExecId ? 'Executando' : chatExecution ? 'Execução' : 'Workflow'}
                  </span>
                </div>
                {chatExecution && (
                  <span className="text-[11px] text-[#3E5F7D] font-mono">{chatExecution.executionId.slice(0, 8)}</span>
                )}
                {/* Prev / Next navigation between executions */}
                {chatExecHistory.length > 1 && (() => {
                  const currentIdx = selectedExecHistoryIdx ?? chatExecHistory.length - 1
                  const hasPrev = currentIdx > 0
                  const hasNext = currentIdx < chatExecHistory.length - 1
                  const navigateTo = (idx: number) => {
                    const execId = chatExecHistory[idx]
                    setSelectedExecHistoryIdx(idx)
                    api.getExecution(execId).then(setChatExecution).catch(console.error)
                    api.getNodes(execId).then(nodes => {
                      const map: Record<string, NodeRecord> = {}
                      nodes.forEach(n => { map[n.nodeId] = n })
                      setChatNodeStates(map)
                    }).catch(console.error)
                  }
                  return (
                    <div className="flex items-center gap-1 ml-1">
                      <button
                        disabled={!hasPrev}
                        onClick={() => navigateTo(currentIdx - 1)}
                        className="p-0.5 rounded text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38] disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                        title="Execução anterior"
                      >
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M15 18l-6-6 6-6"/></svg>
                      </button>
                      <span className="text-[10px] text-[#3E5F7D] font-mono tabular-nums">{currentIdx + 1}/{chatExecHistory.length}</span>
                      <button
                        disabled={!hasNext}
                        onClick={() => navigateTo(currentIdx + 1)}
                        className="p-0.5 rounded text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38] disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                        title="Próxima execução"
                      >
                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M9 18l6-6-6-6"/></svg>
                      </button>
                    </div>
                  )
                })()}
                <div className="flex-1" />
                <button
                  onClick={() => setShowChatMonitor(false)}
                  className="text-[#4A6B8A] hover:text-[#B8CEE5] p-1 rounded hover:bg-[#0C1D38] transition-colors"
                  title="Fechar monitor"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M18 6L6 18M6 6l12 12" />
                  </svg>
                </button>
              </div>

              {/* Execution panel only (no diagram) */}
              <div className="flex-1 overflow-hidden">
                <ExecutionPanel
                  execution={chatExecution}
                  nodes={Object.values(chatNodeStates)}
                  events={chatEvents}
                  auditEvents={chatAuditEvents}
                  toolInvocations={chatToolInvocations}
                  compact
                />
              </div>

              <style>{`@keyframes pulse-dot { 0%,100% { opacity: 1; } 50% { opacity: 0.4; } }`}</style>
            </div>
          )}

          {/* Toggle button when monitor is hidden */}
          {!showChatMonitor && chatWorkflow && (
            <button
              onClick={() => setShowChatMonitor(true)}
              className="absolute right-4 top-16 z-10 flex items-center gap-2 px-3 py-2 bg-[#0C1D38] hover:bg-[#1A3357] border border-[#1A3357] rounded-lg text-xs text-[#B8CEE5] transition-colors shadow-lg"
              title="Mostrar monitor"
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <rect x="2" y="3" width="20" height="14" rx="2" /><path d="M8 21h8M12 17v4" />
              </svg>
              Monitor
            </button>
          )}
        </div>
      ) : mode === 'admin' ? (
        <AdminPanel />
      ) : mode === 'schedule' ? (
        <SchedulePanel />
      ) : mode === 'monitor' ? (
        <div className="flex flex-1 overflow-hidden">
          {/* Collapsible execution list */}
          {showExecList && (
            <div className="w-[320px] border-r border-[#0C1D38] bg-[#04091A] overflow-hidden flex flex-col shrink-0">
              <div className="flex items-center justify-between px-4 py-2 border-b border-[#0C1D38] shrink-0">
                <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Execucoes</span>
                <button
                  onClick={() => setShowExecList(false)}
                  className="text-[#4A6B8A] hover:text-[#B8CEE5] p-1 rounded hover:bg-[#0C1D38] transition-colors"
                  title="Recolher lista"
                >
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M15 18l-6-6 6-6" />
                  </svg>
                </button>
              </div>
              <ExecutionsMonitor
                workflows={workflows}
                selectedExecId={selectedExec?.executionId ?? null}
                onSelect={handleMonitorSelect}
                onStatsChange={setExecStats}
              />
            </div>
          )}
          {!showExecList && (
            <button
              onClick={() => setShowExecList(true)}
              className="shrink-0 w-8 flex flex-col items-center justify-center border-r border-[#0C1D38] bg-[#04091A] hover:bg-[#081529] transition-colors"
              title="Expandir lista"
            >
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="text-[#4A6B8A]">
                <path d="M9 18l6-6-6-6" />
              </svg>
            </button>
          )}
          {/* Main dashboard area */}
          <MonitorDashboard
            workflows={workflows}
            execStats={execStats}
            selectedExec={selectedExec}
            nodeStates={nodeStates}
            events={events}
            auditEvents={auditEvents}
            onSelectExec={handleMonitorSelect}
          />
        </div>
      ) : (
        <div className="flex flex-1 overflow-hidden">
          {selectedWf && (
            <div className="flex-1 overflow-hidden">
              <WorkflowCanvas workflow={selectedWf} nodeStates={nodeStates} onNodeClick={handleNodeClick} />
            </div>
          )}

          {/* Empty state quando nenhum workflow selecionado */}
          {!selectedWf && (
            <div className="flex-1 flex flex-col items-center justify-center text-[#4A6B8A] gap-3">
              <div className="w-12 h-12 rounded-xl bg-[#0C1D38] flex items-center justify-center">
                <span className="text-xl text-[#3E5F7D]">?</span>
              </div>
              <p className="text-sm">Selecione um workflow para visualizar</p>
            </div>
          )}

          {/* ExecutionPanel: 360px fixo à direita */}
          {selectedWf && (
            <div className="w-[360px] border-l border-[#0C1D38] bg-[#04091A] overflow-hidden">
              <ExecutionPanel
                execution={selectedExec}
                nodes={Object.values(nodeStates)}
                events={events}
                auditEvents={auditEvents}
                toolInvocations={toolInvocations}
              />
            </div>
          )}
        </div>
      )}

      {/* Agent detail modal */}
      {(selectedAgent || agentLoading) && (
        <AgentDetailModal
          agent={selectedAgent}
          loading={agentLoading}
          onClose={() => { setSelectedAgent(null); setAgentLoading(false) }}
        />
      )}
    </div>
  )
}
