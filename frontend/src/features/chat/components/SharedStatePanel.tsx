import { useState } from 'react'
import { cn } from '../../../shared/utils/cn'
import { Badge } from '../../../shared/ui/Badge'
import { JsonViewer } from '../../../shared/data/JsonViewer'

interface SharedStatePanelProps {
  agentState: Record<string, Record<string, unknown>>
  changedPaths: Set<string>
  timestamp?: string | null
  isStreaming: boolean
}

function formatAgentName(key: string): string {
  return key
    .replace(/-/g, ' ')
    .replace(/\b\w/g, (c) => c.toUpperCase())
}

function renderValue(value: unknown, isHighlighted: boolean) {
  if (typeof value === 'boolean') {
    return (
      <Badge variant={value ? 'green' : 'red'}>
        {value ? '✓ true' : '✗ false'}
      </Badge>
    )
  }
  if (typeof value === 'string' || typeof value === 'number') {
    return (
      <span className={cn('text-text-primary', isHighlighted && 'font-medium')}>
        {String(value)}
      </span>
    )
  }
  if (value !== null && typeof value === 'object') {
    return <JsonViewer data={value} collapsed={false} maxHeight="150px" className="mt-1" />
  }
  return <span className="text-text-dimmed">—</span>
}

function AgentSection({
  agentKey,
  fields,
  changedPaths,
}: {
  agentKey: string
  fields: Record<string, unknown>
  changedPaths: Set<string>
}) {
  const [expanded, setExpanded] = useState(true)

  return (
    <div className="border-t border-border-primary first:border-t-0">
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-white/5 cursor-pointer"
      >
        <Badge variant="purple" className="text-[10px]">
          {formatAgentName(agentKey)}
        </Badge>
        <span className="flex-1" />
        <span className="text-text-muted text-xs">{expanded ? '▾' : '▸'}</span>
      </button>

      <div
        className={cn(
          'grid transition-[grid-template-rows] duration-200',
          expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]',
        )}
      >
        <div className="overflow-hidden">
          <div className="px-3 pb-2 flex flex-col gap-1">
            {Object.entries(fields).map(([key, value]) => {
              const path = `${agentKey}.${key}`
              const isHighlighted = changedPaths.has(path)
              return (
                <div
                  key={key}
                  className={cn(
                    'flex items-baseline gap-3 rounded px-2 py-0.5 transition-colors duration-1000',
                    isHighlighted && 'bg-accent-blue/10',
                  )}
                >
                  <span className="text-[11px] text-text-muted min-w-[80px] flex-shrink-0 font-mono">
                    {key}:
                  </span>
                  <span className="text-xs">{renderValue(value, isHighlighted)}</span>
                </div>
              )
            })}
          </div>
        </div>
      </div>
    </div>
  )
}

export function SharedStatePanel({ agentState, changedPaths, timestamp, isStreaming }: SharedStatePanelProps) {
  const [expanded, setExpanded] = useState(true)

  return (
    <div>
      <div className="w-full rounded-xl bg-bg-secondary border border-border-primary overflow-hidden text-xs">
        {/* Header */}
        <button
          onClick={() => setExpanded(!expanded)}
          className="w-full flex items-center gap-2 px-3 py-2 text-left hover:bg-white/5 cursor-pointer"
        >
          <svg
            xmlns="http://www.w3.org/2000/svg"
            viewBox="0 0 20 20"
            fill="currentColor"
            className="w-3.5 h-3.5 text-text-muted flex-shrink-0"
          >
            <path
              fillRule="evenodd"
              d="M4.5 2A1.5 1.5 0 003 3.5v13A1.5 1.5 0 004.5 18h11a1.5 1.5 0 001.5-1.5V7.621a1.5 1.5 0 00-.44-1.06l-4.12-4.122A1.5 1.5 0 0011.378 2H4.5zm4.75 6.75a.75.75 0 00-1.5 0v2.546l-.943-1.048a.75.75 0 10-1.114 1.004l2.25 2.5a.75.75 0 001.114 0l2.25-2.5a.75.75 0 00-1.114-1.004l-.943 1.048V8.75z"
              clipRule="evenodd"
            />
          </svg>
          <span className="font-medium text-text-primary flex-1">Estado dos Agentes</span>
          {isStreaming && <Badge variant="blue" pulse>Ao vivo</Badge>}
          {!expanded && timestamp && (
            <span className="text-[10px] text-text-dimmed">{timestamp}</span>
          )}
          <span className="text-text-muted flex-shrink-0">{expanded ? '▾' : '▸'}</span>
        </button>

        {/* Collapsible body */}
        <div
          className={cn(
            'grid transition-[grid-template-rows] duration-200',
            expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]',
          )}
        >
          <div className="overflow-hidden">
            {Object.entries(agentState).map(([agentKey, fields]) => (
              <AgentSection
                key={agentKey}
                agentKey={agentKey}
                fields={fields as Record<string, unknown>}
                changedPaths={changedPaths}
              />
            ))}
            {timestamp && (
              <div className="px-3 pb-1.5 text-right border-t border-border-primary pt-1">
                <span className="text-[10px] text-text-dimmed">Atualizado: {timestamp}</span>
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
