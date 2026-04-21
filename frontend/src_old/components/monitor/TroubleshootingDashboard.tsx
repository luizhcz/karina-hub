import { useState, useEffect, useMemo, useRef } from 'react'
import { api, toolsApi, tokenApi } from '../../api'
import { ExecutionPanel } from '../ExecutionPanel'
import type { WorkflowDef, WorkflowExecution, NodeRecord, ExecutionEventRecord, ToolInvocation, LlmTokenUsage } from '../../types'
import type { TestCase } from './TestCasesPanel'

interface Props {
  selectedExec: WorkflowExecution | null
  nodeStates: Record<string, NodeRecord>
  events: { type: string; payload: unknown; ts: number }[]
  auditEvents: ExecutionEventRecord[]
  onSelectExec: (exec: WorkflowExecution) => void
  workflows: WorkflowDef[]
  onAddTestCase?: (tc: TestCase) => void
}

interface UnifiedEvent {
  ts: number
  type: 'node' | 'tool' | 'llm' | 'event'
  label: string
  detail: string
  color: string
  // For expand support
  itemId?: number
  llmUsage?: LlmTokenUsage
  toolInvocation?: ToolInvocation
}

function getErrorCategory(msg: string): string {
  const colonIdx = msg.indexOf(':')
  if (colonIdx > 0 && colonIdx < 40) return msg.slice(0, colonIdx).trim()
  return msg.split(' ').slice(0, 3).join(' ').trim() || 'Unknown'
}

export function TroubleshootingDashboard({ selectedExec, nodeStates, events, auditEvents, onSelectExec, workflows, onAddTestCase }: Props) {
  const [executions, setExecutions] = useState<WorkflowExecution[]>([])
  const [failedExecutions, setFailedExecutions] = useState<WorkflowExecution[]>([])
  const [toolInvocations, setToolInvocations] = useState<ToolInvocation[]>([])
  const [tokenUsages, setTokenUsages] = useState<LlmTokenUsage[]>([])
  const [errorFilter, setErrorFilter] = useState<string | null>(null)
  const [dismissedErrors, setDismissedErrors] = useState<Set<string>>(new Set())
  const [expandedLlm, setExpandedLlm] = useState<number | null>(null)
  const [expandedTool, setExpandedTool] = useState<number | null>(null)
  const [showTestCaseDialog, setShowTestCaseDialog] = useState(false)
  const [expectedOutput, setExpectedOutput] = useState('')
  const [handoffCollapsed, setHandoffCollapsed] = useState(false)
  const [timelineCollapsed, setTimelineCollapsed] = useState(false)
  const workflowsRef = useRef(workflows)
  workflowsRef.current = workflows
  const wfCount = workflows.length

  // Fetch recent executions for selector
  useEffect(() => {
    const wfs = workflowsRef.current
    if (wfs.length === 0) return
    Promise.all(wfs.map(wf => api.getWorkflowExecutions(wf.id, undefined, 10)))
      .then(results => {
        const all = results.flat().sort((a, b) =>
          new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
        )
        setExecutions(all)
      })
      .catch(console.error)
  }, [wfCount])

  // Fetch failed executions for error patterns (with polling 30s)
  useEffect(() => {
    const fetchFailed = () => {
      api.getAllExecutions({ status: 'Failed', pageSize: 50 })
        .then(({ items }) => setFailedExecutions(items))
        .catch(() => {/* silently ignore */})
    }
    fetchFailed()
    const interval = setInterval(fetchFailed, 30_000)
    return () => clearInterval(interval)
  }, [])

  // Fetch tool invocations + token usages for selected execution
  useEffect(() => {
    if (!selectedExec) { setToolInvocations([]); setTokenUsages([]); return }
    const id = selectedExec.executionId
    toolsApi.getByExecution(id).then(setToolInvocations).catch(() => setToolInvocations([]))
    tokenApi.getByExecution(id).then(setTokenUsages).catch(() => setTokenUsages([]))
  }, [selectedExec?.executionId])

  const nodes = useMemo(() => Object.values(nodeStates), [nodeStates])

  // Build error patterns from failed executions
  const errorPatterns = useMemo(() => {
    const map: Record<string, WorkflowExecution[]> = {}
    for (const exec of failedExecutions) {
      if (!exec.errorMessage) continue
      const cat = getErrorCategory(exec.errorMessage)
      if (!map[cat]) map[cat] = []
      map[cat].push(exec)
    }
    return Object.entries(map)
      .sort((a, b) => b[1].length - a[1].length)
      .slice(0, 5)
  }, [failedExecutions])

  // Filtered executions based on errorFilter
  const filteredExecutions = useMemo(() => {
    if (!errorFilter) return executions
    return executions.filter(ex => {
      if (!ex.errorMessage) return false
      return getErrorCategory(ex.errorMessage) === errorFilter
    })
  }, [executions, errorFilter])

  // Build handoff chain from nodes
  const handoffChain = useMemo(() => {
    const agentNodes = nodes
      .filter(n => n.nodeType === 'agent' && n.startedAt)
      .sort((a, b) => new Date(a.startedAt!).getTime() - new Date(b.startedAt!).getTime())

    return agentNodes.map(n => ({
      agentId: n.nodeId,
      startedAt: n.startedAt!,
      completedAt: n.completedAt,
      status: n.status,
      durationMs: n.completedAt
        ? new Date(n.completedAt).getTime() - new Date(n.startedAt!).getTime()
        : null,
    }))
  }, [nodes])

  // Build unified timeline
  const unifiedTimeline = useMemo((): UnifiedEvent[] => {
    const items: UnifiedEvent[] = []

    for (const n of nodes) {
      if (n.startedAt) {
        items.push({
          ts: new Date(n.startedAt).getTime(),
          type: 'node', label: `${n.nodeId} started`,
          detail: n.nodeType, color: 'text-blue-400',
        })
      }
      if (n.completedAt) {
        items.push({
          ts: new Date(n.completedAt).getTime(),
          type: 'node', label: `${n.nodeId} completed`,
          detail: n.status, color: n.status === 'failed' ? 'text-red-400' : 'text-emerald-400',
        })
      }
    }

    for (const inv of toolInvocations) {
      items.push({
        ts: new Date(inv.createdAt).getTime(),
        type: 'tool', label: `${inv.toolName}`,
        detail: `${inv.success ? 'ok' : 'FAIL'} ${Math.round(inv.durationMs)}ms`,
        color: inv.success ? 'text-[#4D8EF5]' : 'text-red-400',
        itemId: inv.id,
        toolInvocation: inv,
      })
    }

    for (const u of tokenUsages) {
      items.push({
        ts: new Date(u.createdAt).getTime(),
        type: 'llm', label: `LLM ${u.agentId}`,
        detail: `${u.totalTokens}t ${(u.durationMs / 1000).toFixed(1)}s`,
        color: 'text-amber-400',
        itemId: u.id,
        llmUsage: u,
      })
    }

    return items.sort((a, b) => a.ts - b.ts)
  }, [nodes, toolInvocations, tokenUsages])

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* Error Patterns Panel */}
      {errorPatterns.filter(([cat]) => !dismissedErrors.has(cat)).length > 0 && (
        <div className="px-4 pt-3 shrink-0">
          <div className="rounded-xl bg-red-500/5 border border-red-500/10 p-3 mb-4">
            <div className="flex items-center justify-between mb-2">
              <div className="text-[10px] text-red-400/70 uppercase tracking-wider">Padrões de Erro Recentes</div>
              <button
                onClick={() => setDismissedErrors(new Set(errorPatterns.map(([cat]) => cat)))}
                className="text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors"
                title="Limpar todos"
              >
                Limpar
              </button>
            </div>
            <div className="space-y-1">
              {errorPatterns.filter(([cat]) => !dismissedErrors.has(cat)).map(([cat, execs]) => (
                <div key={cat} className="flex items-center gap-3 text-xs">
                  <span className="w-1.5 h-1.5 rounded-full bg-red-500/60 shrink-0" />
                  <span className="text-red-300/80 truncate flex-1">{cat}</span>
                  <span className="text-red-400 font-mono font-bold text-[11px] shrink-0 w-6 text-right">{execs.length}x</span>
                  <button
                    onClick={() => setErrorFilter(errorFilter === cat ? null : cat)}
                    className="text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] shrink-0 transition-colors"
                  >
                    filtrar
                  </button>
                  <button
                    onClick={() => setDismissedErrors(prev => new Set([...prev, cat]))}
                    className="text-[10px] text-[#4A6B8A] hover:text-red-400 shrink-0 transition-colors"
                    title="Dispensar"
                  >
                    ✕
                  </button>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* Execution selector */}
      <div className="flex items-center gap-3 px-4 py-2.5 border-b border-[#0C1D38] shrink-0 bg-[#04091A]">
        <span className="text-xs font-medium text-[#7596B8] uppercase tracking-wider shrink-0">Execucao</span>
        {/* Prev / Next navigation — scoped to same conversation when available */}
        {(() => {
          const convId = selectedExec?.metadata?.conversationId
          const list = convId
            ? executions.filter(x => x.metadata?.conversationId === convId)
            : filteredExecutions.length > 0 ? filteredExecutions : executions
          const idx = selectedExec ? list.findIndex(x => x.executionId === selectedExec.executionId) : -1
          const hasPrev = idx > 0
          const hasNext = idx !== -1 && idx < list.length - 1
          return (
            <div className="flex items-center gap-1">
              <button
                disabled={!hasPrev}
                onClick={() => onSelectExec(list[idx - 1])}
                className="p-0.5 rounded text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38] disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                title={convId ? 'Execução anterior desta conversa' : 'Execução anterior'}
              >
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M15 18l-6-6 6-6"/></svg>
              </button>
              {idx !== -1 && (
                <span
                  className="text-[10px] font-mono tabular-nums"
                  style={{ color: convId ? '#4D8EF5' : '#3E5F7D' }}
                  title={convId ? `Conversa: ${convId.slice(0, 8)}` : undefined}
                >
                  {idx + 1}/{list.length}
                </span>
              )}
              <button
                disabled={!hasNext}
                onClick={() => onSelectExec(list[idx + 1])}
                className="p-0.5 rounded text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#0C1D38] disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
                title={convId ? 'Próxima execução desta conversa' : 'Próxima execução'}
              >
                <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><path d="M9 18l6-6-6-6"/></svg>
              </button>
            </div>
          )
        })()}
        <select
          className="flex-1 max-w-sm bg-[#0C1D38] border border-[#1A3357] rounded-md px-2.5 py-1.5 text-sm text-[#DCE8F5] focus:border-[#0057E0] focus:ring-1 focus:ring-[#0057E030] outline-none"
          value={selectedExec?.executionId ?? ''}
          onChange={e => {
            const exec = filteredExecutions.find(x => x.executionId === e.target.value)
              ?? executions.find(x => x.executionId === e.target.value)
            if (exec) onSelectExec(exec)
          }}
        >
          <option value="">Selecione...</option>
          {filteredExecutions.map(ex => (
            <option key={ex.executionId} value={ex.executionId}>
              {ex.status} | {new Date(ex.startedAt).toLocaleTimeString()} | {ex.executionId.slice(0, 8)}
            </option>
          ))}
        </select>
        {errorFilter && (
          <span className="flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-md bg-red-500/10 text-red-300 border border-red-500/20">
            Filtrando: {errorFilter}
            <button
              onClick={() => setErrorFilter(null)}
              className="ml-1 text-red-400 hover:text-red-200 transition-colors"
            >
              ✕
            </button>
          </span>
        )}
        {selectedExec && (
          <span className={`text-[11px] px-2 py-0.5 rounded-md font-medium ${
            selectedExec.status === 'Completed' ? 'bg-emerald-500/20 text-emerald-300' :
            selectedExec.status === 'Failed' ? 'bg-red-500/20 text-red-300' :
            selectedExec.status === 'Running' ? 'bg-blue-500/20 text-blue-300' :
            'bg-[#081529] text-[#B8CEE5]'
          }`}>
            {selectedExec.status}
          </span>
        )}
        {selectedExec && onAddTestCase && (
          <button
            onClick={() => { setExpectedOutput(selectedExec.output ?? ''); setShowTestCaseDialog(true) }}
            className="ml-auto px-2.5 py-1 rounded-md text-[11px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#B8CEE5] transition-colors shrink-0"
            title="Salvar esta execução como caso de teste"
          >
            + Test Case
          </button>
        )}
      </div>

      {/* Test case dialog */}
      {showTestCaseDialog && selectedExec && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60" onClick={() => setShowTestCaseDialog(false)}>
          <div className="bg-[#081529] border border-[#1A3357] rounded-lg p-6 w-full max-w-lg mx-4" onClick={e => e.stopPropagation()}>
            <div className="text-sm font-semibold text-[#DCE8F5] mb-1">Salvar como Caso de Teste</div>
            <div className="text-[11px] text-[#4A6B8A] mb-4 font-mono truncate">
              {selectedExec.workflowId} · {selectedExec.executionId.slice(0, 12)}
            </div>
            <div className="mb-3">
              <div className="text-[11px] text-[#4A6B8A] uppercase tracking-wider mb-1">Input</div>
              <div className="bg-[#04091A] border border-[#0C1D38] rounded-md px-3 py-2 text-[12px] text-[#7596B8] font-mono max-h-20 overflow-y-auto">
                {selectedExec.input?.slice(0, 300) ?? '(vazio)'}
              </div>
            </div>
            <div className="mb-4">
              <div className="text-[11px] text-[#4A6B8A] uppercase tracking-wider mb-1">Output Esperado <span className="text-[#ff4444]">*</span></div>
              <textarea
                value={expectedOutput}
                onChange={e => setExpectedOutput(e.target.value)}
                placeholder="Cole ou edite o output esperado para este input…"
                className="w-full bg-[#04091A] border border-[#1A3357] rounded-md px-3 py-2 text-[12px] text-[#DCE8F5] font-mono placeholder:text-[#444] focus:border-[#254980] focus:outline-none transition-colors resize-y min-h-[80px]"
              />
            </div>
            <div className="flex gap-2 justify-end">
              <button
                onClick={() => setShowTestCaseDialog(false)}
                className="px-3.5 py-[6px] rounded-md text-[12px] font-medium border border-[#1A3357] text-[#4A6B8A] hover:border-[#254980] hover:text-[#999] transition-colors"
              >
                Cancelar
              </button>
              <button
                disabled={!expectedOutput.trim()}
                onClick={() => {
                  const wf = workflows.find(w => w.id === selectedExec.workflowId)
                  onAddTestCase!({
                    id: `tc-${Date.now()}`,
                    workflowId: selectedExec.workflowId,
                    workflowName: wf?.name ?? selectedExec.workflowId,
                    input: selectedExec.input ?? '',
                    expectedOutput: expectedOutput.trim(),
                    promptVersionId: tokenUsages[0]?.promptVersionId,
                    savedAt: new Date().toISOString(),
                  })
                  setShowTestCaseDialog(false)
                  setExpectedOutput('')
                }}
                className="px-4 py-[7px] rounded-md text-[12px] font-medium bg-white text-black hover:bg-[#e0e0e0] transition-colors disabled:opacity-40"
              >
                Salvar
              </button>
            </div>
          </div>
        </div>
      )}

      {selectedExec ? (
        <div className="flex-1 flex flex-col overflow-hidden">
          {/* Execution panel (existing component) */}
          <div className="flex-1 min-h-0 overflow-hidden border-b border-[#0C1D38]">
            <ExecutionPanel
              execution={selectedExec}
              nodes={nodes}
              events={events}
              auditEvents={auditEvents}
              toolInvocations={toolInvocations}
            />
          </div>

          {/* Bottom row: handoff chain + correlated timeline */}
          {handoffCollapsed && timelineCollapsed && (
            <div className="shrink-0 flex items-center gap-2 px-3 border-t border-[#0C1D38]" style={{ height: 28 }}>
              <button onClick={() => setHandoffCollapsed(false)} className="flex items-center gap-1.5 text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors">
                <span>▶</span><span className="uppercase tracking-wider">Handoff Chain</span>
              </button>
              <span className="text-[#1A3357]">|</span>
              <button onClick={() => setTimelineCollapsed(false)} className="flex items-center gap-1.5 text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors">
                <span>▶</span><span className="uppercase tracking-wider">Timeline Correlacionada</span>
              </button>
            </div>
          )}
          <div className={`shrink-0 flex overflow-hidden border-t border-[#0C1D38] ${handoffCollapsed && timelineCollapsed ? 'hidden' : ''}`} style={{ height: 280 }}>
            {/* Handoff chain */}
            {handoffCollapsed ? (
              <button
                onClick={() => setHandoffCollapsed(false)}
                className="shrink-0 w-7 flex flex-col items-center justify-center gap-1 border-r border-[#0C1D38] hover:bg-[#081529] transition-colors"
                title="Expandir Handoff Chain"
              >
                <span className="text-[#4A6B8A] text-[9px]">▶</span>
                <span className="text-[9px] text-[#4A6B8A] uppercase tracking-widest" style={{ writingMode: 'vertical-rl' }}>Handoff</span>
              </button>
            ) : (
              <div className={`flex flex-col overflow-hidden border-r border-[#0C1D38] ${timelineCollapsed ? 'flex-1' : 'w-1/2'}`}>
                <div className="flex items-center justify-between px-3 py-1.5 shrink-0">
                  <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">Handoff Chain</div>
                  <button
                    onClick={() => setHandoffCollapsed(true)}
                    className="text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors p-0.5"
                    title="Minimizar"
                  >▲</button>
                </div>
                <div className="flex-1 overflow-y-auto px-3 pb-3">
                  {handoffChain.length > 0 ? (
                    <div className="space-y-1">
                      {handoffChain.map((step, i) => (
                        <div key={i} className="flex items-center gap-2">
                          {i > 0 && (
                            <div className="flex flex-col items-center w-4 shrink-0">
                              <div className="w-px h-3 bg-[#1A3357]" />
                              <span className="text-[8px] text-[#3E5F7D]">&darr;</span>
                              <div className="w-px h-3 bg-[#1A3357]" />
                            </div>
                          )}
                          {i === 0 && <div className="w-4 shrink-0" />}
                          <div className={`flex-1 flex items-center gap-2 px-2.5 py-1.5 rounded-md border ${
                            step.status === 'running' ? 'border-blue-500/30 bg-blue-500/5' :
                            step.status === 'completed' ? 'border-emerald-500/30 bg-emerald-500/5' :
                            step.status === 'failed' ? 'border-red-500/30 bg-red-500/5' :
                            'border-[#1A3357] bg-[#081529]/30'
                          }`}>
                            <span className={`w-2 h-2 rounded-full shrink-0 ${
                              step.status === 'running' ? 'bg-blue-400 animate-pulse' :
                              step.status === 'completed' ? 'bg-emerald-400' :
                              step.status === 'failed' ? 'bg-red-400' : 'bg-[#3E5F7D]'
                            }`} />
                            <span className="text-[11px] text-[#DCE8F5] font-mono font-medium truncate">{step.agentId}</span>
                            {step.durationMs != null && (
                              <span className="text-[10px] text-[#4A6B8A] font-mono shrink-0 ml-auto">
                                {(step.durationMs / 1000).toFixed(1)}s
                              </span>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  ) : (
                    <div className="text-center text-[#3E5F7D] text-xs py-4">Sem dados de handoff</div>
                  )}
                </div>
              </div>
            )}

            {/* Correlated timeline */}
            {timelineCollapsed ? (
              <button
                onClick={() => setTimelineCollapsed(false)}
                className="shrink-0 w-7 flex flex-col items-center justify-center gap-1 hover:bg-[#081529] transition-colors"
                title="Expandir Timeline Correlacionada"
              >
                <span className="text-[#4A6B8A] text-[9px]">▶</span>
                <span className="text-[9px] text-[#4A6B8A] uppercase tracking-widest" style={{ writingMode: 'vertical-rl' }}>Timeline</span>
              </button>
            ) : (
              <div className={`flex flex-col overflow-hidden ${handoffCollapsed ? 'flex-1' : 'w-1/2'}`}>
                <div className="flex items-center justify-between px-3 py-1.5 shrink-0">
                  <div className="text-xs font-medium text-[#7596B8] uppercase tracking-wider">
                    Timeline Correlacionada ({unifiedTimeline.length})
                  </div>
                  <button
                    onClick={() => setTimelineCollapsed(true)}
                    className="text-[10px] text-[#4A6B8A] hover:text-[#B8CEE5] transition-colors p-0.5"
                    title="Minimizar"
                  >▲</button>
                </div>
                <div className="flex-1 overflow-y-auto px-3 pb-3">
              {unifiedTimeline.length > 0 ? (
                <div className="space-y-0.5">
                  {unifiedTimeline.map((evt, i) => {
                    if (evt.type === 'llm' && evt.llmUsage) {
                      const usage = evt.llmUsage
                      const isExpanded = expandedLlm === usage.id
                      return (
                        <div key={i}>
                          <div
                            className="flex items-center gap-2 px-2 py-1 rounded hover:bg-[#081529]/30 cursor-pointer select-none"
                            onClick={() => setExpandedLlm(isExpanded ? null : usage.id)}
                          >
                            <span className="text-[9px] text-[#3E5F7D] font-mono w-16 shrink-0">
                              {new Date(evt.ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                            </span>
                            <span className="text-[10px] font-mono w-10 shrink-0 uppercase text-amber-500">llm</span>
                            <span className={`text-[11px] font-mono ${evt.color} truncate`}>{evt.label}</span>
                            {usage.promptVersionId && (
                              <span className="text-[9px] px-1 py-0.5 rounded bg-violet-500/15 text-violet-300 border border-violet-500/20 font-mono shrink-0">
                                [{usage.promptVersionId}]
                              </span>
                            )}
                            <span className="text-[10px] text-[#3E5F7D] font-mono shrink-0 ml-auto">{evt.detail}</span>
                            <span className="text-[9px] text-[#3E5F7D] shrink-0">{isExpanded ? '▲' : '▼'}</span>
                          </div>
                          {isExpanded && usage.outputContent && (
                            <div className="mx-2 mb-1 border-l-2 border-violet-500/40 pl-2">
                              <pre
                                className="text-[10px] font-mono text-[#B8CEE5] whitespace-pre-wrap break-all overflow-y-auto"
                                style={{ background: '#04091A', maxHeight: 200, padding: '6px 8px', borderRadius: 4 }}
                              >
                                {usage.outputContent}
                              </pre>
                            </div>
                          )}
                          {isExpanded && !usage.outputContent && (
                            <div className="mx-2 mb-1 text-[10px] text-[#3E5F7D] pl-4 italic">Sem output content</div>
                          )}
                        </div>
                      )
                    }

                    if (evt.type === 'tool' && evt.toolInvocation) {
                      const tool = evt.toolInvocation
                      const isExpanded = expandedTool === tool.id

                      let formattedArgs = tool.arguments ?? ''
                      if (formattedArgs) {
                        try {
                          formattedArgs = JSON.stringify(JSON.parse(formattedArgs), null, 2)
                        } catch {
                          // fallback to raw
                        }
                      }
                      const truncatedResult = tool.result
                        ? tool.result.length > 500 ? tool.result.slice(0, 500) + '…' : tool.result
                        : ''

                      return (
                        <div key={i}>
                          <div
                            className="flex items-center gap-2 px-2 py-1 rounded hover:bg-[#081529]/30 cursor-pointer select-none"
                            onClick={() => setExpandedTool(isExpanded ? null : tool.id)}
                          >
                            <span className="text-[9px] text-[#3E5F7D] font-mono w-16 shrink-0">
                              {new Date(evt.ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                            </span>
                            <span className="text-[10px] font-mono w-10 shrink-0 uppercase text-[#4D8EF5]">tool</span>
                            <span className={`text-[11px] font-mono ${evt.color} truncate`}>{evt.label}</span>
                            <span className="text-[10px] text-[#3E5F7D] font-mono shrink-0 ml-auto">{evt.detail}</span>
                            <span className="text-[9px] text-[#3E5F7D] shrink-0">{isExpanded ? '▲' : '▼'}</span>
                          </div>
                          {isExpanded && (
                            <div className="mx-2 mb-1 border-l-2 border-[#4D8EF5]/40 pl-2 space-y-1">
                              {formattedArgs && (
                                <div>
                                  <div className="text-[9px] text-[#4A6B8A] uppercase tracking-wider mb-0.5">Args:</div>
                                  <pre
                                    className="text-[10px] font-mono text-[#B8CEE5] whitespace-pre-wrap break-all overflow-y-auto"
                                    style={{ background: '#04091A', maxHeight: 120, padding: '4px 8px', borderRadius: 4 }}
                                  >
                                    {formattedArgs}
                                  </pre>
                                </div>
                              )}
                              {truncatedResult && (
                                <div>
                                  <div className="text-[9px] text-[#4A6B8A] uppercase tracking-wider mb-0.5">Result:</div>
                                  <pre
                                    className="text-[10px] font-mono text-[#B8CEE5] whitespace-pre-wrap break-all overflow-y-auto"
                                    style={{ background: '#04091A', maxHeight: 120, padding: '4px 8px', borderRadius: 4 }}
                                  >
                                    {truncatedResult}
                                  </pre>
                                </div>
                              )}
                              {!formattedArgs && !truncatedResult && (
                                <div className="text-[10px] text-[#3E5F7D] italic">Sem dados de args/result</div>
                              )}
                            </div>
                          )}
                        </div>
                      )
                    }

                    return (
                      <div key={i} className="flex items-center gap-2 px-2 py-1 rounded hover:bg-[#081529]/30">
                        <span className="text-[9px] text-[#3E5F7D] font-mono w-16 shrink-0">
                          {new Date(evt.ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                        </span>
                        <span className={`text-[10px] font-mono w-10 shrink-0 uppercase ${
                          evt.type === 'node' ? 'text-blue-500' :
                          evt.type === 'tool' ? 'text-[#4D8EF5]' :
                          evt.type === 'llm' ? 'text-amber-500' :
                          'text-[#4A6B8A]'
                        }`}>{evt.type}</span>
                        <span className={`text-[11px] font-mono ${evt.color} truncate`}>{evt.label}</span>
                        <span className="text-[10px] text-[#3E5F7D] font-mono shrink-0 ml-auto">{evt.detail}</span>
                      </div>
                    )
                  })}
                </div>
              ) : (
                <div className="text-center text-[#3E5F7D] text-xs py-4">Selecione uma execucao para ver a timeline</div>
              )}
                </div>
              </div>
            )}
          </div>
        </div>
      ) : (
        <div className="flex-1 flex flex-col items-center justify-center text-[#4A6B8A] gap-3">
          <div className="w-12 h-12 rounded-xl bg-[#0C1D38] flex items-center justify-center">
            <span className="text-xl text-[#3E5F7D]">?</span>
          </div>
          <p className="text-sm font-medium text-[#7596B8]">Selecione uma execucao para investigar</p>
          <p className="text-xs text-[#3E5F7D]">Use o seletor acima ou a lista de execucoes a esquerda</p>
        </div>
      )}
    </div>
  )
}
