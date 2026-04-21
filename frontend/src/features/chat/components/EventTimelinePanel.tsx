import { useState, useRef, useEffect } from 'react'
import { cn } from '../../../shared/utils/cn'
import { Badge } from '../../../shared/ui/Badge'
import type { TimelineEvent } from '../hooks/useAgUiEventTimeline'

interface EventTimelinePanelProps {
  events: TimelineEvent[]
  isStreaming: boolean
}

const TYPE_COLORS: Record<string, 'gray' | 'green' | 'yellow' | 'purple' | 'red' | 'blue'> = {
  RUN_STARTED: 'gray',
  RUN_FINISHED: 'gray',
  RUN_ERROR: 'red',
  STEP_STARTED: 'blue',
  STEP_FINISHED: 'blue',
  TEXT_MESSAGE_START: 'green',
  TEXT_MESSAGE_CONTENT: 'green',
  TEXT_MESSAGE_END: 'green',
  TOOL_CALL_START: 'yellow',
  TOOL_CALL_ARGS: 'yellow',
  TOOL_CALL_END: 'yellow',
  TOOL_CALL_RESULT: 'yellow',
  STATE_SNAPSHOT: 'purple',
  STATE_DELTA: 'purple',
  MESSAGES_SNAPSHOT: 'gray',
}

function getTypeColor(type: string) {
  return TYPE_COLORS[type] ?? 'gray'
}

function formatTime(ts: number): string {
  const d = new Date(ts)
  return d.toLocaleTimeString('pt-BR', {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    fractionalSecondDigits: 3,
  } as Intl.DateTimeFormatOptions)
}

function truncateJson(data: Record<string, unknown>, maxLen = 120): string {
  try {
    const str = JSON.stringify(data)
    return str.length > maxLen ? str.slice(0, maxLen) + '...' : str
  } catch {
    return '{...}'
  }
}

export function EventTimelinePanel({ events, isStreaming }: EventTimelinePanelProps) {
  const [expanded, setExpanded] = useState(false)
  const scrollRef = useRef<HTMLDivElement>(null)

  // Auto-scroll to bottom when new events arrive
  useEffect(() => {
    if (expanded && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [events.length, expanded])

  // Filter out high-frequency TEXT_MESSAGE_CONTENT for display (show count instead)
  const displayEvents = events.filter((e) => e.type !== 'TEXT_MESSAGE_CONTENT')
  const textContentCount = events.filter((e) => e.type === 'TEXT_MESSAGE_CONTENT').length

  return (
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
            d="M10 18a8 8 0 100-16 8 8 0 000 16zm.75-13a.75.75 0 00-1.5 0v5c0 .414.336.75.75.75h4a.75.75 0 000-1.5h-3.25V5z"
            clipRule="evenodd"
          />
        </svg>
        <span className="font-medium text-text-primary flex-1">Event Timeline</span>
        {isStreaming && <Badge variant="blue" pulse>Ao vivo</Badge>}
        <Badge variant="gray">{events.length}</Badge>
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
          <div ref={scrollRef} className="max-h-[300px] overflow-y-auto">
            {displayEvents.length === 0 && (
              <div className="px-3 py-4 text-center text-text-dimmed">
                Nenhum evento ainda.
              </div>
            )}
            {displayEvents.map((evt) => (
              <div
                key={evt.id}
                className="flex items-start gap-2 px-3 py-1 border-t border-border-primary first:border-t-0 hover:bg-white/5"
              >
                <span className="text-[10px] text-text-dimmed font-mono min-w-[85px] flex-shrink-0 mt-0.5">
                  {formatTime(evt.timestamp)}
                </span>
                <Badge variant={getTypeColor(evt.type)} className="text-[9px] flex-shrink-0 mt-0.5">
                  {evt.type}
                </Badge>
                <span className="text-[10px] text-text-muted font-mono break-all line-clamp-2">
                  {truncateJson(evt.data)}
                </span>
              </div>
            ))}
            {textContentCount > 0 && (
              <div className="px-3 py-1 border-t border-border-primary text-text-dimmed text-[10px]">
                + {textContentCount} TEXT_MESSAGE_CONTENT events (hidden)
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}
