import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { useExecutionFull } from '../../api/executions'
import type { NodeRecord, ToolInvocation, WorkflowExecution } from '../../api/executions'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { StatusPill } from '../../shared/data/StatusPill'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EmptyState } from '../../shared/ui/EmptyState'
import { formatDuration } from '../../shared/utils/formatters'

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)
  const copy = () => {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }
  return (
    <button
      onClick={copy}
      title="Copiar"
      className="flex-none ml-1 text-[10px] px-1 py-0.5 rounded bg-bg-tertiary border border-border-secondary text-text-muted hover:text-text-primary hover:border-accent-blue transition-colors"
    >
      {copied ? '✓' : '⎘'}
    </button>
  )
}

// ── Waterfall ─────────────────────────────────────────────────────────────────

function pct(ms: number, total: number) {
  return total > 0 ? Math.min((ms / total) * 100, 100) : 0
}

function timeLabel(ms: number) {
  if (ms < 1000) return `${Math.round(ms)}ms`
  return `${(ms / 1000).toFixed(1)}s`
}

function TimeRuler({ totalMs }: { totalMs: number }) {
  const ticks = [0, 0.25, 0.5, 0.75, 1]
  return (
    <div className="relative h-5 mb-1 ml-[180px]">
      {ticks.map((t) => (
        <span
          key={t}
          className="absolute text-[10px] text-text-muted -translate-x-1/2"
          style={{ left: `${t * 100}%` }}
        >
          {timeLabel(t * totalMs)}
        </span>
      ))}
      {/* Tick lines */}
      {ticks.map((t) => (
        <span
          key={`line-${t}`}
          className="absolute top-4 h-1 w-px bg-border-primary"
          style={{ left: `${t * 100}%` }}
        />
      ))}
    </div>
  )
}

interface SpanRowProps {
  label: string
  sublabel?: string
  leftPct: number
  widthPct: number
  color: string
  durationMs: number
  indent?: boolean
  badge?: string
}

function SpanRow({ label, sublabel, leftPct, widthPct, color, durationMs, indent, badge }: SpanRowProps) {
  const safeWidth = Math.max(widthPct, 0.5)

  return (
    <div className="flex items-center gap-2 py-0.5 group">
      {/* Label */}
      <div className={`flex-none w-[180px] flex items-center gap-1.5 ${indent ? 'pl-5' : ''}`}>
        {indent && <span className="text-border-primary text-xs">└</span>}
        <div className="min-w-0">
          <p className="text-xs text-text-primary truncate font-mono">{label}</p>
          {sublabel && <p className="text-[10px] text-text-muted truncate">{sublabel}</p>}
        </div>
        {badge && (
          <span className="flex-none text-[9px] px-1 py-0.5 rounded bg-red-500/20 text-red-400 font-medium">
            {badge}
          </span>
        )}
      </div>

      {/* Bar track */}
      <div className="flex-1 relative h-5">
        <div
          className={`absolute top-1 h-3 rounded-sm ${color} opacity-80 transition-all`}
          style={{ left: `${leftPct}%`, width: `${safeWidth}%` }}
        />
        {/* Duration label on hover */}
        <div
          className="absolute top-1 h-3 flex items-center pointer-events-none opacity-0 group-hover:opacity-100 transition-opacity"
          style={{ left: `calc(${leftPct}% + ${safeWidth}% + 4px)` }}
        >
          <span className="text-[10px] text-text-muted whitespace-nowrap">
            {timeLabel(durationMs)}
          </span>
        </div>
      </div>
    </div>
  )
}

function Waterfall({
  execution,
  nodes,
  tools,
}: {
  execution: WorkflowExecution
  nodes: NodeRecord[]
  tools: ToolInvocation[]
}) {
  const t0 = new Date(execution.startedAt).getTime()
  const tEnd = execution.completedAt
    ? new Date(execution.completedAt).getTime()
    : Date.now()
  const totalMs = Math.max(tEnd - t0, 1)

  // Sort nodes by startedAt
  const sortedNodes = [...nodes].sort((a, b) => {
    const ta = a.startedAt ? new Date(a.startedAt).getTime() : 0
    const tb = b.startedAt ? new Date(b.startedAt).getTime() : 0
    return ta - tb
  })

  return (
    <div className="font-mono">
      <TimeRuler totalMs={totalMs} />

      {/* Vertical grid lines */}
      <div className="relative">
        <div className="absolute inset-y-0 left-[180px] right-0 pointer-events-none">
          {[0.25, 0.5, 0.75].map((t) => (
            <div
              key={t}
              className="absolute inset-y-0 w-px bg-border-primary/30"
              style={{ left: `${t * 100}%` }}
            />
          ))}
        </div>

        {/* Rows */}
        <div className="flex flex-col">
          {sortedNodes.map((node) => {
            const nodeStart = node.startedAt ? new Date(node.startedAt).getTime() : t0
            const nodeEnd = node.completedAt ? new Date(node.completedAt).getTime() : tEnd
            const nodeMs = nodeEnd - nodeStart
            const nodeLeft = pct(nodeStart - t0, totalMs)
            const nodeWidth = pct(nodeMs, totalMs)

            const nodeColor =
              node.status === 'completed' ? 'bg-emerald-500' :
              node.status === 'failed' ? 'bg-red-500' :
              node.status === 'running' ? 'bg-blue-400' :
              'bg-text-muted'

            // Tools belonging to this node
            const nodeTools = tools
              .filter((t) => t.agentId === node.nodeId)
              .sort((a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime())

            return (
              <div key={`${node.nodeId}-${node.iteration}`}>
                <SpanRow
                  label={node.nodeId}
                  sublabel={node.iteration > 1 ? `iteration ${node.iteration}` : node.nodeType}
                  leftPct={nodeLeft}
                  widthPct={nodeWidth}
                  color={nodeColor}
                  durationMs={nodeMs}
                  badge={node.status === 'failed' ? 'FAIL' : undefined}
                />
                {nodeTools.map((tool) => {
                  const toolStart = new Date(tool.createdAt).getTime()
                  const toolLeft = pct(toolStart - t0, totalMs)
                  const toolWidth = pct(tool.durationMs, totalMs)
                  const toolColor = tool.success ? 'bg-violet-400' : 'bg-red-400'

                  return (
                    <SpanRow
                      key={tool.id}
                      label={tool.toolName}
                      leftPct={toolLeft}
                      widthPct={toolWidth}
                      color={toolColor}
                      durationMs={tool.durationMs}
                      indent
                      badge={!tool.success ? 'ERR' : undefined}
                    />
                  )
                })}
              </div>
            )
          })}
        </div>
      </div>

      {/* Legend */}
      <div className="flex gap-4 mt-3 pt-3 border-t border-border-primary/30 flex-wrap">
        {[
          { color: 'bg-emerald-500', label: 'Node completed' },
          { color: 'bg-blue-400', label: 'Node running' },
          { color: 'bg-red-500', label: 'Node failed' },
          { color: 'bg-violet-400', label: 'Tool call' },
          { color: 'bg-red-400', label: 'Tool error' },
        ].map(({ color, label }) => (
          <span key={label} className="flex items-center gap-1.5 text-[10px] text-text-muted">
            <span className={`w-3 h-2 rounded-sm ${color} opacity-80`} />
            {label}
          </span>
        ))}
      </div>
    </div>
  )
}

// ── Event log ─────────────────────────────────────────────────────────────────

function EventTypeChip({ type }: { type: string }) {
  const color =
    type.includes('error') || type.includes('failed') ? 'bg-red-500/15 text-red-400' :
    type.includes('completed') || type.includes('finished') ? 'bg-emerald-500/15 text-emerald-400' :
    type.includes('started') || type.includes('started') ? 'bg-blue-500/15 text-blue-400' :
    type.includes('hitl') ? 'bg-amber-500/15 text-amber-400' :
    'bg-bg-tertiary text-text-muted'

  return (
    <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded font-mono ${color}`}>
      {type}
    </span>
  )
}

// ── Main ─────────────────────────────────────────────────────────────────────

export function TracingDetailPage() {
  const { traceId } = useParams<{ traceId: string }>()
  const { data, isLoading, error, refetch } = useExecutionFull(traceId ?? '', !!traceId)

  if (isLoading) return <PageLoader />
  if (error || !data) return <ErrorCard message="Execução não encontrada" onRetry={refetch} />

  const { execution, nodes, tools, events } = data

  const t0 = new Date(execution.startedAt).getTime()
  const tEnd = execution.completedAt
    ? new Date(execution.completedAt).getTime()
    : Date.now()
  const totalMs = tEnd - t0

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex items-start gap-4">
        <Link to="/tracing">
          <Button variant="ghost" size="sm">← Voltar</Button>
        </Link>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-3 flex-wrap">
            <h1 className="text-xl font-bold text-text-primary">{execution.workflowId}</h1>
            <StatusPill status={execution.status} />
          </div>
          <p className="font-mono text-xs text-text-muted mt-1">{execution.executionId}</p>
        </div>
      </div>

      {/* Metrics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        {[
          { label: 'Duração total', value: formatDuration(totalMs) },
          { label: 'Início', value: new Date(execution.startedAt).toLocaleString('pt-BR') },
          { label: 'Fim', value: execution.completedAt ? new Date(execution.completedAt).toLocaleString('pt-BR') : '—' },
          { label: 'Nodes / Tools', value: `${nodes.length} / ${tools.length}` },
        ].map(({ label, value }) => (
          <div key={label} className="bg-bg-secondary border border-border-primary rounded-xl p-4">
            <p className="text-[10px] text-text-muted uppercase tracking-wide">{label}</p>
            <p className="text-sm font-semibold text-text-primary mt-1">{value}</p>
          </div>
        ))}
      </div>

      {/* Waterfall */}
      <Card title="Trace Waterfall">
        {nodes.length === 0 ? (
          <EmptyState
            title="Sem nodes registrados"
            description="Nenhum node de execução foi registrado para esta execução."
          />
        ) : (
          <div className="overflow-x-auto">
            <div className="min-w-[560px]">
              <Waterfall execution={execution} nodes={nodes} tools={tools} />
            </div>
          </div>
        )}
      </Card>

      {/* Tool invocations */}
      {tools.length > 0 && (
        <Card title={`Tool Invocations (${tools.length})`} padding={false}>
          <div className="divide-y divide-border-primary/40">
            <div className="grid grid-cols-[140px_100px_80px_80px_1fr] gap-3 px-4 py-2 text-[10px] font-semibold text-text-muted uppercase tracking-wider">
              <span>Tool</span>
              <span>Agent</span>
              <span>Status</span>
              <span>Duration</span>
              <span>Resultado</span>
            </div>
            {tools.map((tool) => (
              <div
                key={tool.id}
                className="grid grid-cols-[140px_100px_80px_80px_1fr] gap-3 px-4 py-2.5 items-start"
              >
                <span className="font-mono text-xs text-text-primary truncate">{tool.toolName}</span>
                <span className="text-xs text-text-muted truncate">{tool.agentId}</span>
                <span className={`text-[10px] font-medium ${tool.success ? 'text-emerald-400' : 'text-red-400'}`}>
                  {tool.success ? 'OK' : 'ERRO'}
                </span>
                <span className="text-xs text-text-muted tabular-nums">{timeLabel(tool.durationMs)}</span>
                <div className="flex items-start gap-1 min-w-0">
                  <span className="text-xs text-text-muted font-mono truncate">
                    {tool.result ?? tool.errorMessage ?? '—'}
                  </span>
                  {(tool.result || tool.errorMessage) && (
                    <CopyButton text={tool.result ?? tool.errorMessage ?? ''} />
                  )}
                </div>
              </div>
            ))}
          </div>
        </Card>
      )}

      {/* Event log */}
      <Card title={`Event Log (${events.length})`} padding={false}>
        {events.length === 0 ? (
          <EmptyState title="Sem eventos" description="Nenhum evento auditado." />
        ) : (
          <div className="divide-y divide-border-primary/40">
            <div className="grid grid-cols-[160px_200px_1fr] gap-3 px-4 py-2 text-[10px] font-semibold text-text-muted uppercase tracking-wider">
              <span>Timestamp</span>
              <span>Tipo</span>
              <span>Payload</span>
            </div>
            {events.map((ev, i) => (
              <div key={i} className="grid grid-cols-[160px_200px_1fr] gap-3 px-4 py-2 items-start">
                <span className="text-[10px] text-text-muted tabular-nums">
                  {new Date(ev.timestamp).toLocaleTimeString('pt-BR', {
                    hour: '2-digit',
                    minute: '2-digit',
                    second: '2-digit',
                    fractionalSecondDigits: 3,
                  })}
                </span>
                <EventTypeChip type={ev.eventType} />
                <div className="flex items-start gap-1 min-w-0">
                  <span className="text-[10px] font-mono text-text-muted truncate">{ev.payload}</span>
                  {ev.payload && <CopyButton text={ev.payload} />}
                </div>
              </div>
            ))}
          </div>
        )}
      </Card>

      {/* Error */}
      {execution.errorMessage && (
        <Card title="Erro">
          <pre className="text-xs text-red-400 whitespace-pre-wrap break-all font-mono">
            {execution.errorMessage}
          </pre>
        </Card>
      )}
    </div>
  )
}
