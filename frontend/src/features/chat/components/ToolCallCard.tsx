import { useState, useEffect } from 'react'
import { cn } from '../../../shared/utils/cn'
import { formatDuration } from '../../../shared/utils/formatters'
import type { LocalMsg } from '../types'

type ToolCallMsg = Extract<LocalMsg, { kind: 'tool-call' }>

function prettyJson(raw?: string) {
  if (!raw) return null
  try {
    const parsed = JSON.parse(raw)
    return JSON.stringify(parsed, null, 2)
  } catch {
    return raw
  }
}

export function ToolCallCard({ item }: { item: ToolCallMsg }) {
  const [expanded, setExpanded] = useState(!item.done)

  // Auto-collapse when done
  useEffect(() => {
    if (item.done) setExpanded(false)
  }, [item.done])

  const duration =
    item.startedAt && item.endedAt
      ? formatDuration(item.endedAt - item.startedAt)
      : null

  const argsJson = prettyJson(item.args)
  const resultJson = prettyJson(item.result)
  const hasBody = !!(argsJson || resultJson)

  return (
    <div className="flex justify-start">
      <div className="max-w-[80%] rounded-lg bg-bg-tertiary border border-border-primary overflow-hidden text-xs">
        <button
          onClick={() => hasBody && setExpanded(!expanded)}
          className={cn(
            'w-full flex items-center gap-2 px-3 py-2 text-left',
            hasBody && 'hover:bg-white/5 cursor-pointer',
            !hasBody && 'cursor-default',
          )}
        >
          <span
            className={cn(
              'w-2 h-2 rounded-full flex-shrink-0',
              item.done ? 'bg-green-500' : 'bg-accent-blue animate-pulse',
            )}
          />
          <span className="font-medium text-text-primary flex-1 truncate">
            {item.done ? `✓ ${item.toolName}` : `Chamando ${item.toolName}…`}
          </span>
          {duration && (
            <span className="text-text-dimmed flex-shrink-0">{duration}</span>
          )}
          {hasBody && (
            <span className="text-text-muted flex-shrink-0">
              {expanded ? '▾' : '▸'}
            </span>
          )}
        </button>

        <div
          className={cn(
            'grid transition-[grid-template-rows] duration-200',
            expanded ? 'grid-rows-[1fr]' : 'grid-rows-[0fr]',
          )}
        >
          <div className="overflow-hidden">
            <div className="px-3 pb-3 flex flex-col gap-2 border-t border-border-primary pt-2">
              {argsJson && (
                <div>
                  <p className="text-[10px] text-text-muted uppercase tracking-wide mb-1">Argumentos</p>
                  <pre className="text-[11px] text-text-secondary bg-bg-primary/50 rounded p-2 overflow-auto max-h-[200px] font-mono whitespace-pre-wrap break-all">
                    {argsJson}
                  </pre>
                </div>
              )}
              {resultJson && (
                <div>
                  <p className="text-[10px] text-text-muted uppercase tracking-wide mb-1">Resultado</p>
                  <pre className="text-[11px] text-text-secondary bg-bg-primary/50 rounded p-2 overflow-auto max-h-[200px] font-mono whitespace-pre-wrap break-all">
                    {resultJson}
                  </pre>
                </div>
              )}
            </div>
          </div>
        </div>

        {item.done && item.endedAt && (
          <div className="px-3 pb-1.5 text-right">
            <span className="text-[10px] text-text-dimmed">
              {new Date(item.endedAt).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' })}
            </span>
          </div>
        )}
      </div>
    </div>
  )
}
