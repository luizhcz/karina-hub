import { useState, useEffect, useRef, useMemo } from 'react'
import { tokenApi } from '../api'
import type { WorkflowExecution, NodeRecord, ExecutionEventRecord, LlmTokenUsage, ToolInvocation } from '../types'

interface Props {
  execution: WorkflowExecution | null
  nodes: NodeRecord[]
  events: { type: string; payload: unknown; ts: number }[]
  auditEvents: ExecutionEventRecord[]
  toolInvocations?: ToolInvocation[]
  /** Hides execution header and output section (used in chat monitor) */
  compact?: boolean
}

const STATUS_BADGE: Record<string, string> = {
  Running:   'bg-yellow-500/20 text-yellow-300',
  Completed: 'bg-green-500/20 text-green-300',
  Failed:    'bg-red-500/20 text-red-300',
  Pending:   'bg-gray-500/20 text-gray-300',
  Cancelled: 'bg-gray-500/20 text-gray-300',
  Paused:    'bg-blue-500/20 text-blue-300',
}

type Tab = 'live' | 'timeline' | 'audit' | 'tokens'

export function ExecutionPanel({ execution, nodes, events, auditEvents, toolInvocations = [], compact }: Props) {
  const [tab, setTab] = useState<Tab>('live')
  const [tokenUsages, setTokenUsages] = useState<LlmTokenUsage[]>([])
  const eventsEndRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (tab === 'live')
      eventsEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [events.length, tab])

  // Track seen execution IDs to avoid re-fetching
  const fetchedExecIds = useRef(new Set<string>())

  useEffect(() => {
    if (!execution) return
    const execId = execution.executionId
    const isTerminal = execution.status === 'Completed' || execution.status === 'Failed' || execution.status === 'Cancelled'

    const fetchTokens = () => {
      tokenApi.getByExecution(execId).then(newUsages => {
        if (compact) {
          if (newUsages.length > 0)
            setTokenUsages(prev => {
              const existingIds = new Set(prev.map(u => u.id))
              const fresh = newUsages.filter(u => !existingIds.has(u.id))
              return fresh.length > 0 ? [...prev, ...fresh] : prev
            })
        } else {
          setTokenUsages(newUsages)
        }
      }).catch(() => { if (!compact) setTokenUsages([]) })
    }

    if (compact) {
      const key = `${execId}:${execution.status}`
      if (fetchedExecIds.current.has(key)) return
      fetchedExecIds.current.add(key)
    }

    if (isTerminal) {
      // Delay to allow fire-and-forget token persistence to complete, then retry
      const t1 = setTimeout(fetchTokens, 1200)
      const t2 = setTimeout(fetchTokens, 3500)
      return () => { clearTimeout(t1); clearTimeout(t2) }
    } else {
      fetchTokens()
    }
  }, [execution?.executionId, execution?.status, compact])

  // Consolidate consecutive token events into single stream entries
  const consolidatedEvents = useMemo(() => {
    const result: { type: string; payload: unknown; ts: number; tokenText?: string; tokenCount?: number }[] = []
    for (const e of events) {
      if (e.type === 'token') {
        const text = (e.payload as Record<string, unknown>)?.text as string ?? ''
        const last = result[result.length - 1]
        if (last?.type === 'token') {
          last.tokenText = (last.tokenText ?? '') + text
          last.tokenCount = (last.tokenCount ?? 1) + 1
        } else {
          result.push({ ...e, tokenText: text, tokenCount: 1 })
        }
      } else {
        result.push(e)
      }
    }
    return result
  }, [events])

  // Consolidate audit events: merge consecutive token events
  const consolidatedAudit = useMemo(() => {
    const result: (ExecutionEventRecord & { mergedText?: string; mergedCount?: number })[] = []
    for (const e of auditEvents) {
      if (e.eventType === 'token') {
        let text = ''
        try { text = (JSON.parse(e.payload) as { text?: string }).text ?? '' } catch { /* */ }
        const last = result[result.length - 1]
        if (last?.eventType === 'token') {
          last.mergedText = (last.mergedText ?? '') + text
          last.mergedCount = (last.mergedCount ?? 1) + 1
        } else {
          result.push({ ...e, mergedText: text, mergedCount: 1 })
        }
      } else {
        result.push(e)
      }
    }
    return result
  }, [auditEvents])

  // Unified audit timeline: audit events + tool invocations sorted by timestamp
  type AuditItem =
    | { kind: 'event'; event: ExecutionEventRecord & { mergedText?: string; mergedCount?: number } }
    | { kind: 'tool'; tool: ToolInvocation }

  const unifiedTimeline = useMemo((): AuditItem[] => {
    const items: AuditItem[] = [
      ...consolidatedAudit.map(e => ({ kind: 'event' as const, event: e, _ts: new Date(e.timestamp).getTime() })),
      ...toolInvocations.map(t => ({ kind: 'tool' as const, tool: t, _ts: new Date(t.createdAt).getTime() })),
    ]
    items.sort((a, b) => a._ts - b._ts)
    return items
  }, [consolidatedAudit, toolInvocations])

  return (
    <div className="flex flex-col h-full overflow-hidden text-sm">
      {/* Header — hidden in compact mode */}
      {!compact && execution && (
        <div className="px-4 py-3 border-b border-[#0C1D38] shrink-0">
          <div className="flex items-center justify-between mb-2">
            <span className={`px-2.5 py-1 rounded-md text-xs font-semibold ${STATUS_BADGE[execution.status] ?? ''}`}>
              {execution.status}
            </span>
            <span className="text-[#4A6B8A] text-[11px] font-mono bg-[#0C1D38] px-2 py-0.5 rounded">{execution.executionId.slice(0, 12)}</span>
          </div>
          {execution.startedAt && (
            <div className="text-xs text-[#7596B8] flex items-center gap-1.5">
              <span>{new Date(execution.startedAt).toLocaleTimeString()}</span>
              {execution.completedAt && (
                <>
                  <span className="text-[#3E5F7D]">&#8594;</span>
                  <span>{new Date(execution.completedAt).toLocaleTimeString()}</span>
                  <span className="text-[#4A6B8A] bg-[#0C1D38] px-1.5 py-0.5 rounded text-[11px] font-medium ml-1">
                    {((new Date(execution.completedAt).getTime() - new Date(execution.startedAt).getTime()) / 1000).toFixed(1)}s
                  </span>
                </>
              )}
            </div>
          )}
        </div>
      )}

      {/* Tabs */}
      <div className="flex border-b border-[#0C1D38] shrink-0 px-2 gap-1">
        <TabButton label="Live" active={tab === 'live'} count={consolidatedEvents.length} onClick={() => setTab('live')} />
        <TabButton label="Timeline" active={tab === 'timeline'} count={nodes.length} onClick={() => setTab('timeline')} />
        <TabButton label="Tokens" active={tab === 'tokens'} count={tokenUsages.length} onClick={() => setTab('tokens')} />
        <TabButton label="Audit" active={tab === 'audit'} count={unifiedTimeline.length} onClick={() => setTab('audit')} />
      </div>

      {/* Tab content */}
      {tab === 'live' ? (
        <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
          {consolidatedEvents.length === 0 ? (
            <EmptyState icon="▸" title="Aguardando eventos" subtitle="Execute o workflow para ver eventos em tempo real." />
          ) : (
            <div className="space-y-1">
              {consolidatedEvents.map((e, i) => (
                e.type === '__separator__' ? (
                  <div key={i} className="flex items-center gap-3 py-2">
                    <div className="flex-1 h-px bg-[#1A3357]/60" />
                    <span className="text-[10px] text-[#4A6B8A] font-mono shrink-0">
                      {((e.payload as Record<string, unknown>)?.executionId as string ?? '').slice(0, 8)}
                    </span>
                    <div className="flex-1 h-px bg-[#1A3357]/60" />
                  </div>
                ) : (
                  <LiveEventRow key={i} type={e.type} payload={e.payload} ts={e.ts} tokenText={e.tokenText} tokenCount={e.tokenCount} />
                )
              ))}
              <div ref={eventsEndRef} />
            </div>
          )}
        </div>
      ) : tab === 'timeline' ? (
        <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
          {nodes.length === 0 ? (
            <EmptyState icon="─" title="Nenhum node executado" subtitle="A timeline aparece quando os nodes iniciam." />
          ) : (
            <GanttChart nodes={nodes} />
          )}
        </div>
      ) : tab === 'tokens' ? (
        <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
          {tokenUsages.length === 0 ? (
            <EmptyState icon="T" title="Nenhum uso de tokens registrado" subtitle="Tokens aparecem quando agentes LLM executam." />
          ) : (
            <TokenUsageTable usages={tokenUsages} />
          )}
        </div>
      ) : (
        <div className="flex-1 overflow-y-auto px-4 py-3 min-h-0">
          {unifiedTimeline.length === 0 ? (
            <EmptyState icon="?" title="Nenhum evento persistido" subtitle="Execute o workflow para gerar eventos de auditoria." />
          ) : (
            <div className="space-y-1.5">
              {unifiedTimeline.map((item, i) => {
                if (item.kind === 'tool') {
                  return <ToolInvocationRow key={`tool-${item.tool.id}`} tool={item.tool} />
                }
                const e = item.event
                if (e.eventType === '__separator__') {
                  return (
                    <div key={i} className="flex items-center gap-3 py-2">
                      <div className="flex-1 h-px bg-[#1A3357]/60" />
                      <span className="text-[10px] text-[#4A6B8A] font-mono shrink-0">
                        {(() => { try { return (JSON.parse(e.payload) as Record<string, string>).executionId?.slice(0, 8) } catch { return '' } })()}
                      </span>
                      <div className="flex-1 h-px bg-[#1A3357]/60" />
                    </div>
                  )
                }
                return <AuditEventRow key={i} event={e} mergedText={e.mergedText} mergedCount={e.mergedCount} />
              })}
            </div>
          )}
        </div>
      )}

      {/* Output — hidden in compact mode */}
      {!compact && execution?.output && (
        <details className="border-t border-[#0C1D38] shrink-0 group">
          <summary className="px-4 py-2.5 text-[11px] text-[#4A6B8A] uppercase tracking-wider font-semibold cursor-pointer hover:text-[#B8CEE5] hover:bg-[#081529]/30 transition-colors flex items-center justify-between">
            Output
            <span className="text-[10px] text-[#3E5F7D] group-open:rotate-180 transition-transform">&#9660;</span>
          </summary>
          <div className="px-4 pb-3 max-h-48 overflow-y-auto">
            <pre className="text-xs text-[#B8CEE5] whitespace-pre-wrap font-mono leading-relaxed bg-[#081529]/30 rounded-md p-3">{execution.output}</pre>
          </div>
        </details>
      )}
    </div>
  )
}

function EmptyState({ icon, title, subtitle }: { icon: string; title: string; subtitle: string }) {
  return (
    <div className="flex flex-col items-center justify-center py-12 text-[#4A6B8A]">
      <div className="w-10 h-10 rounded-lg bg-[#0C1D38] flex items-center justify-center mb-3">
        <span className="text-lg text-[#3E5F7D]">{icon}</span>
      </div>
      <p className="text-sm font-medium text-[#7596B8]">{title}</p>
      <p className="text-xs text-[#3E5F7D] mt-1">{subtitle}</p>
    </div>
  )
}

function TabButton({ label, active, count, onClick }: {
  label: string; active: boolean; count: number; onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      className={`flex-1 py-2 text-xs font-semibold transition-all rounded-t-md ${
        active
          ? 'text-[#DCE8F5] border-b-2 border-[#0057E0] bg-[#081529]/40'
          : 'text-[#4A6B8A] hover:text-[#B8CEE5] hover:bg-[#081529]/20 border-b-2 border-transparent'
      }`}
    >
      {label}
      {count > 0 && (
        <span className={`ml-1.5 text-[10px] px-1.5 py-0.5 rounded-full ${active ? 'bg-[#0057E015] text-[#0057E0]' : 'bg-[#0C1D38] text-[#4A6B8A]'}`}>
          {count}
        </span>
      )}
    </button>
  )
}

function GanttChart({ nodes }: { nodes: NodeRecord[] }) {
  const [expandedNode, setExpandedNode] = useState<string | null>(null)
  const sorted = [...nodes].sort(
    (a, b) => new Date(a.startedAt ?? 0).getTime() - new Date(b.startedAt ?? 0).getTime()
  )

  const t0 = sorted.reduce((min, n) => {
    const t = n.startedAt ? new Date(n.startedAt).getTime() : Infinity
    return t < min ? t : min
  }, Infinity)

  const tEnd = sorted.reduce((max, n) => {
    const t = n.completedAt
      ? new Date(n.completedAt).getTime()
      : n.startedAt ? new Date(n.startedAt).getTime() + 500
      : 0
    return t > max ? t : max
  }, t0 + 1000)

  const totalMs = Math.max(tEnd - t0, 1)
  const LABEL_W = 120

  return (
    <div>
      {sorted.map(n => {
        const start = n.startedAt ? new Date(n.startedAt).getTime() : t0
        const end = n.completedAt
          ? new Date(n.completedAt).getTime()
          : n.status === 'running' ? Date.now()
          : start

        const leftPct = ((start - t0) / totalMs) * 100
        const widthPct = Math.max(((end - start) / totalMs) * 100, 0.5)

        const barColor =
          n.status === 'running'   ? 'bg-amber-400 animate-pulse' :
          n.status === 'completed' ? 'bg-emerald-500' :
          n.status === 'failed'    ? 'bg-red-500' :
                                     'bg-[#254980]'

        const duration = n.startedAt && n.completedAt
          ? ((new Date(n.completedAt).getTime() - new Date(n.startedAt).getTime()) / 1000).toFixed(1) + 's'
          : n.status === 'running' ? '…' : null

        const tokenLabel = n.tokensUsed ? `~${n.tokensUsed}t` : null
        const isExpanded = expandedNode === n.nodeId

        return (
          <div key={n.nodeId} className="mb-1">
            <button
              onClick={() => setExpandedNode(isExpanded ? null : n.nodeId)}
              className="w-full flex items-center gap-2.5 hover:bg-[#081529]/30 rounded-md px-1 py-1 transition-colors"
            >
              <div className="shrink-0 text-right" style={{ width: LABEL_W }}>
                <span className="text-[11px] text-[#B8CEE5] font-mono truncate block font-medium" title={n.nodeId}>
                  {n.nodeId}
                </span>
                {tokenLabel && (
                  <span className="text-[10px] text-[#3E5F7D] font-mono">{tokenLabel}</span>
                )}
              </div>
              <div className="flex-1 relative h-5 rounded-md overflow-hidden">
                <div className="absolute inset-0 bg-[#0C1D38]" />
                <div
                  className={`absolute inset-y-0 rounded-md ${barColor}`}
                  style={{ left: `${leftPct}%`, width: `${widthPct}%` }}
                />
                {duration && (
                  <span
                    className="absolute inset-y-0 flex items-center text-[10px] text-white/80 font-mono font-medium px-1.5"
                    style={{ left: `calc(${leftPct}% + 2px)` }}
                  >
                    {duration}
                  </span>
                )}
              </div>
              <span className={`text-[10px] text-[#4A6B8A] shrink-0 transition-transform ${isExpanded ? 'rotate-180' : ''}`}>&#9660;</span>
            </button>

            {isExpanded && n.output && (
              <div className="ml-4 mr-2 mt-1 mb-2 rounded-md border border-[#1A3357] bg-[#081529]/40 overflow-hidden">
                <div className="px-3 py-1.5 border-b border-[#1A3357]/50">
                  <span className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-semibold">Output</span>
                </div>
                <pre className="px-3 py-2 text-xs text-[#B8CEE5] whitespace-pre-wrap font-mono leading-relaxed max-h-48 overflow-y-auto break-words">{n.output}</pre>
              </div>
            )}
          </div>
        )
      })}
      <div className="flex mt-1.5" style={{ paddingLeft: LABEL_W + 10 }}>
        <span className="text-[10px] text-[#4A6B8A] font-mono">0s</span>
        <div className="flex-1" />
        <span className="text-[10px] text-[#4A6B8A] font-mono">{(totalMs / 1000).toFixed(1)}s</span>
      </div>
    </div>
  )
}

const LIVE_EVENT_CONFIG: Record<string, { icon: string; label: string; color: string; bg: string }> = {
  node_started:       { icon: '▶', label: 'Started',   color: 'text-blue-400',    bg: 'bg-blue-500/8' },
  node_completed:     { icon: '✓', label: 'Completed', color: 'text-green-400',   bg: 'bg-green-500/8' },
  workflow_completed: { icon: '✔', label: 'Done',      color: 'text-emerald-300', bg: 'bg-emerald-500/8' },
  error:              { icon: '✕', label: 'Error',     color: 'text-red-400',     bg: 'bg-red-500/8' },
  token:              { icon: '⊹', label: 'Stream',    color: 'text-[#4D8EF5]',  bg: 'bg-[#0057E0]/8' },
}

function LiveEventRow({ type, payload, ts, tokenText, tokenCount }: {
  type: string; payload: unknown; ts: number; tokenText?: string; tokenCount?: number
}) {
  const [expanded, setExpanded] = useState(false)
  if (type === 'step_completed') return null

  const config = LIVE_EVENT_CONFIG[type] ?? { icon: '·', label: type, color: 'text-[#7596B8]', bg: '' }
  const p = payload as Record<string, unknown>

  const isToken = type === 'token'

  if (isToken && tokenText !== undefined) {
    const preview = tokenText.length > 120 ? tokenText.slice(0, 120) + '…' : tokenText
    return (
      <div className={`rounded-md ${config.bg} transition-all ${expanded ? 'mb-2' : 'mb-1'}`}>
        <button
          onClick={() => setExpanded(v => !v)}
          className="w-full flex items-start gap-2.5 text-left px-2.5 py-2 hover:bg-white/5 rounded-md transition-colors"
        >
          <span className={`text-sm shrink-0 mt-0.5 ${config.color}`}>{config.icon}</span>
          <div className="flex-1 min-w-0">
            <div className="flex items-baseline gap-2">
              <span className={`text-xs font-semibold ${config.color}`}>{config.label}</span>
              <span className="text-[10px] text-[#3E5F7D] font-mono">{tokenCount} tokens</span>
            </div>
            <p className="text-xs text-[#7596B8] font-mono mt-0.5 leading-relaxed break-words">
              {expanded ? tokenText : preview}
            </p>
          </div>
          <div className="flex flex-col items-end shrink-0 gap-1">
            <span className="text-[10px] text-[#3E5F7D] font-mono">
              {new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
            </span>
            <span className={`text-[10px] transition-transform ${config.color} ${expanded ? 'rotate-180' : ''}`}>▼</span>
          </div>
        </button>
      </div>
    )
  }

  const summary =
    type === 'node_started'       ? String(p?.nodeId ?? '')
    : type === 'node_completed'   ? `${p?.nodeId}${p?.tokensUsed ? ` · ${p.tokensUsed} tokens` : ''}`
    : type === 'workflow_completed' ? 'Workflow finalizado com sucesso'
    : type === 'error'            ? (p?.message as string ?? 'Erro desconhecido')
    : ''

  return (
    <div className={`flex items-center gap-2.5 min-w-0 px-2.5 py-2 rounded-md ${config.bg} transition-colors`}>
      <span className={`text-sm shrink-0 ${config.color}`}>{config.icon}</span>
      <div className="flex-1 min-w-0">
        <div className="flex items-baseline gap-2">
          <span className={`text-xs font-semibold ${config.color}`}>{config.label}</span>
          {summary && <span className="text-xs text-[#B8CEE5] truncate">{summary}</span>}
        </div>
        <span className="text-[10px] text-[#4A6B8A] font-mono">
          {new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
        </span>
      </div>
    </div>
  )
}

const EVENT_TYPE_CONFIG: Record<string, { label: string; icon: string; bg: string; text: string; border: string }> = {
  node_started:       { label: 'Node Started',       icon: '▶', bg: 'bg-blue-500/10',    text: 'text-blue-400',    border: 'border-blue-500/30' },
  node_completed:     { label: 'Node Completed',     icon: '✓', bg: 'bg-green-500/10',   text: 'text-green-400',   border: 'border-green-500/30' },
  workflow_completed: { label: 'Workflow Completed',  icon: '✔', bg: 'bg-emerald-500/10', text: 'text-emerald-300', border: 'border-emerald-500/30' },
  error:              { label: 'Error',               icon: '✕', bg: 'bg-red-500/10',     text: 'text-red-400',     border: 'border-red-500/30' },
  token:              { label: 'Stream',              icon: '⊹', bg: 'bg-[#0057E0]/10',  text: 'text-[#4D8EF5]',  border: 'border-[#0057E0]/30' },
  step_completed:     { label: 'Step Completed',      icon: '•', bg: 'bg-[#081529]',   text: 'text-[#4A6B8A]',   border: 'border-[#254980]/50' },
}

const HIDDEN_KEYS = new Set(['executionId', 'timestamp'])

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return value ? 'Yes' : 'No'
  if (typeof value === 'number') return value.toLocaleString()
  if (typeof value === 'string') return value
  return JSON.stringify(value, null, 2)
}

function formatKey(key: string): string {
  return key
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/_/g, ' ')
    .replace(/\b\w/g, c => c.toUpperCase())
}

function PayloadField({ label, value }: { label: string; value: unknown }) {
  const formatted = formatValue(value)
  const isLong = typeof value === 'object' && value !== null
  const isError = label.toLowerCase().includes('error') || label.toLowerCase().includes('message')

  return (
    <div className={`${isLong ? 'col-span-2' : ''}`}>
      <dt className="text-[11px] text-[#4A6B8A] font-medium mb-0.5">{formatKey(label)}</dt>
      <dd className={`text-sm font-mono break-words ${isError ? 'text-red-300' : 'text-[#DCE8F5]'}`}>
        {isLong ? (
          <pre className="text-xs bg-[#081529] rounded p-2 whitespace-pre-wrap overflow-x-auto">{formatted}</pre>
        ) : (
          formatted
        )}
      </dd>
    </div>
  )
}

function ToolInvocationRow({ tool }: { tool: ToolInvocation }) {
  const [expanded, setExpanded] = useState(false)

  let parsedArgs: unknown = tool.arguments
  try { if (tool.arguments) parsedArgs = JSON.parse(tool.arguments) } catch { /* raw */ }

  let parsedResult: unknown = tool.result
  try { if (tool.result) parsedResult = JSON.parse(tool.result) } catch { /* raw */ }

  const bg    = tool.success ? 'bg-[#4D8EF5]/8'    : 'bg-red-500/10'
  const border = tool.success ? 'border-[#4D8EF5]/30' : 'border-red-500/30'
  const text   = tool.success ? 'text-[#4D8EF5]'    : 'text-red-400'

  return (
    <div className={`rounded-lg border ${border} ${bg} transition-all duration-150 ${expanded ? 'mb-2' : 'mb-1'}`}>
      <button
        onClick={() => setExpanded(v => !v)}
        className="w-full flex items-start gap-3 text-left px-3 py-2 hover:bg-white/5 rounded-lg transition-colors"
      >
        <span className={`text-base shrink-0 mt-0.5 ${text}`}>⚙</span>
        <div className="flex-1 min-w-0">
          <div className="flex items-baseline gap-2 flex-wrap">
            <span className={`text-xs font-semibold ${text}`}>Tool Call</span>
            <span className="text-xs text-[#DCE8F5] font-mono font-medium">{tool.toolName}</span>
            <span className="text-[10px] text-[#4A6B8A] font-mono">{tool.agentId}</span>
            <span className={`text-[10px] font-mono ml-auto ${tool.success ? 'text-emerald-400' : 'text-red-400'}`}>
              {tool.success ? '✓' : '✕'} {Math.round(tool.durationMs)}ms
            </span>
          </div>
          <span className="text-[11px] text-[#4A6B8A] font-mono">
            {new Date(tool.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 })}
          </span>
        </div>
        <span className={`text-xs shrink-0 transition-transform duration-150 ${text} ${expanded ? 'rotate-180' : ''}`}>▼</span>
      </button>

      {expanded && (
        <div className="px-3 pb-3 pt-1 border-t border-[#1A3357] space-y-2">
          <div>
            <dt className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5">Arguments</dt>
            <pre className="text-xs text-[#B8CEE5] font-mono bg-[#081529] rounded p-2 whitespace-pre-wrap overflow-x-auto max-h-40 overflow-y-auto">
              {typeof parsedArgs === 'string' ? parsedArgs : JSON.stringify(parsedArgs, null, 2)}
            </pre>
          </div>
          {tool.result && (
            <div>
              <dt className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5">Result</dt>
              <pre className={`text-xs font-mono bg-[#081529] rounded p-2 whitespace-pre-wrap overflow-x-auto max-h-40 overflow-y-auto ${tool.success ? 'text-emerald-300' : 'text-red-300'}`}>
                {typeof parsedResult === 'string' ? parsedResult : JSON.stringify(parsedResult, null, 2)}
              </pre>
            </div>
          )}
          {tool.errorMessage && (
            <div>
              <dt className="text-[10px] text-[#4A6B8A] uppercase tracking-wider font-medium mb-0.5">Error</dt>
              <pre className="text-xs text-red-300 font-mono bg-red-500/5 border border-red-500/10 rounded p-2 whitespace-pre-wrap">{tool.errorMessage}</pre>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function AuditEventRow({ event, mergedText, mergedCount }: {
  event: ExecutionEventRecord; mergedText?: string; mergedCount?: number
}) {
  const [expanded, setExpanded] = useState(false)

  const config = EVENT_TYPE_CONFIG[event.eventType] ?? {
    label: event.eventType, icon: '•', bg: 'bg-[#081529]', text: 'text-[#7596B8]', border: 'border-[#254980]/50',
  }

  const isConsolidatedToken = event.eventType === 'token' && mergedText !== undefined

  let parsed: Record<string, unknown> = {}
  try { parsed = JSON.parse(event.payload) } catch { /* raw */ }

  const summary = isConsolidatedToken
    ? (mergedText!.length > 60 ? mergedText!.slice(0, 60) + '…' : mergedText!)
    : event.eventType === 'node_started'         ? `→ ${parsed.nodeId}`
    : event.eventType === 'node_completed'       ? `✓ ${parsed.nodeId}${parsed.tokensUsed ? ` (~${parsed.tokensUsed}t)` : ''}`
    : event.eventType === 'workflow_completed'   ? 'Workflow complete'
    : event.eventType === 'error'                ? (parsed.message as string ?? 'error')
    : ''

  const visibleFields = isConsolidatedToken ? [] : Object.entries(parsed).filter(([k]) => !HIDDEN_KEYS.has(k))

  return (
    <div className={`rounded-lg border ${config.border} ${config.bg} transition-all duration-150 ${expanded ? 'mb-2' : 'mb-1'}`}>
      <button
        onClick={() => setExpanded(v => !v)}
        className="w-full flex items-start gap-3 text-left px-3 py-2 hover:bg-white/5 rounded-lg transition-colors"
      >
        <span className={`text-base shrink-0 mt-0.5 ${config.text}`}>{config.icon}</span>
        <div className="flex-1 min-w-0">
          <div className="flex items-baseline gap-2">
            <span className={`text-xs font-semibold ${config.text}`}>{config.label}</span>
            {isConsolidatedToken && (
              <span className="text-[10px] text-[#3E5F7D] font-mono">{mergedCount} chunks</span>
            )}
            {!isConsolidatedToken && summary && <span className="text-xs text-[#7596B8] truncate">{summary}</span>}
          </div>
          {isConsolidatedToken && (
            <p className="text-xs text-[#7596B8] font-mono mt-0.5 leading-relaxed break-words">
              {expanded ? mergedText : summary}
            </p>
          )}
          <span className="text-[11px] text-[#4A6B8A] font-mono">
            {new Date(event.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', fractionalSecondDigits: 3 })}
          </span>
        </div>
        <span className={`text-xs shrink-0 transition-transform duration-150 ${config.text} ${expanded ? 'rotate-180' : ''}`}>▼</span>
      </button>

      {expanded && !isConsolidatedToken && visibleFields.length > 0 && (
        <div className="px-3 pb-3 pt-1 border-t border-[#1A3357]">
          <dl className="grid grid-cols-2 gap-x-4 gap-y-2">
            {visibleFields.map(([key, value]) => (
              <PayloadField key={key} label={key} value={value} />
            ))}
          </dl>
        </div>
      )}
    </div>
  )
}

function TokenUsageTable({ usages }: { usages: LlmTokenUsage[] }) {
  const totalInput  = usages.reduce((s, u) => s + u.inputTokens, 0)
  const totalOutput = usages.reduce((s, u) => s + u.outputTokens, 0)
  const totalTokens = usages.reduce((s, u) => s + u.totalTokens, 0)
  const totalDuration = usages.reduce((s, u) => s + u.durationMs, 0)

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-4 gap-2">
        <div className="rounded-md bg-[#0057E0]/10 border border-[#0057E0]/20 px-2.5 py-1.5 text-center">
          <div className="text-[10px] text-[#4A6B8A] uppercase">Total</div>
          <div className="text-sm font-mono font-semibold text-[#4D8EF5]">{totalTokens.toLocaleString()}</div>
        </div>
        <div className="rounded-md bg-blue-500/10 border border-blue-500/20 px-2.5 py-1.5 text-center">
          <div className="text-[10px] text-[#4A6B8A] uppercase">Input</div>
          <div className="text-sm font-mono font-semibold text-blue-400">{totalInput.toLocaleString()}</div>
        </div>
        <div className="rounded-md bg-emerald-500/10 border border-emerald-500/20 px-2.5 py-1.5 text-center">
          <div className="text-[10px] text-[#4A6B8A] uppercase">Output</div>
          <div className="text-sm font-mono font-semibold text-emerald-400">{totalOutput.toLocaleString()}</div>
        </div>
        <div className="rounded-md bg-amber-500/10 border border-amber-500/20 px-2.5 py-1.5 text-center">
          <div className="text-[10px] text-[#4A6B8A] uppercase">Tempo</div>
          <div className="text-sm font-mono font-semibold text-amber-400">{(totalDuration / 1000).toFixed(1)}s</div>
        </div>
      </div>

      <div className="rounded-lg border border-[#1A3357] overflow-hidden">
        <table className="w-full text-xs">
          <thead>
            <tr className="bg-[#081529] text-[#4A6B8A] uppercase text-[10px] tracking-wider">
              <th className="text-left px-3 py-2 font-medium">Agent</th>
              <th className="text-right px-3 py-2 font-medium">Input</th>
              <th className="text-right px-3 py-2 font-medium">Output</th>
              <th className="text-right px-3 py-2 font-medium">Total</th>
              <th className="text-right px-3 py-2 font-medium">Tempo</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-[#0C1D38]">
            {usages.map((u, i) => (
              <tr key={i} className="hover:bg-[#081529]/30 transition-colors">
                <td className="px-3 py-2">
                  <div className="text-[#DCE8F5] font-medium truncate max-w-[120px]">{u.agentId}</div>
                  <div className="text-[10px] text-[#3E5F7D] font-mono">{u.modelId}</div>
                </td>
                <td className="text-right px-3 py-2 text-blue-400 font-mono">{u.inputTokens.toLocaleString()}</td>
                <td className="text-right px-3 py-2 text-emerald-400 font-mono">{u.outputTokens.toLocaleString()}</td>
                <td className="text-right px-3 py-2 text-[#DCE8F5] font-mono font-medium">{u.totalTokens.toLocaleString()}</td>
                <td className="text-right px-3 py-2 text-[#7596B8] font-mono">{(u.durationMs / 1000).toFixed(1)}s</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
