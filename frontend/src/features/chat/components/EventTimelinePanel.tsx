import { useState, useRef, useEffect } from 'react'
import { cn } from '../../../shared/utils/cn'
import { Badge } from '../../../shared/ui/Badge'
import { Modal } from '../../../shared/ui/Modal'
import { JsonViewer } from '../../../shared/data/JsonViewer'
import type { TimelineEvent } from '../hooks/useAgUiEventTimeline'

interface EventTimelinePanelProps {
  events: TimelineEvent[]
  isStreaming: boolean
  /** Callback disparado pelo botão "Limpar" no header — limpa timeline manualmente. */
  onClear?: () => void
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

export function EventTimelinePanel({ events, isStreaming, onClear }: EventTimelinePanelProps) {
  const [expanded, setExpanded] = useState(false)
  const [selected, setSelected] = useState<TimelineEvent | null>(null)
  const scrollRef = useRef<HTMLDivElement>(null)

  // Auto-scroll para o final quando chega novo evento (só se o painel está aberto).
  useEffect(() => {
    if (expanded && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [events.length, expanded])

  // TEXT_MESSAGE_CONTENT é high-frequency (um por token); esconde e mostra só
  // o count para não poluir. Os eventos continuam no buffer e podem ser inspecionados
  // via payload de TEXT_MESSAGE_END (que inclui delta consolidado).
  const displayEvents = events.filter((e) => e.type !== 'TEXT_MESSAGE_CONTENT')
  const textContentCount = events.filter((e) => e.type === 'TEXT_MESSAGE_CONTENT').length

  // Calcula qual é o "número do run" para cada RUN_STARTED — permite mostrar
  // separadores "Run #N" agrupando eventos acumulados de várias interações.
  const runIndexByEventId = new Map<number, number>()
  {
    let runCount = 0
    for (const evt of displayEvents) {
      if (evt.type === 'RUN_STARTED') {
        runCount++
        runIndexByEventId.set(evt.id, runCount)
      }
    }
  }

  return (
    <div className="w-full rounded-xl bg-bg-secondary border border-border-primary overflow-hidden text-xs">
      {/* Header — botão de expand + status + count + Limpar */}
      <div className="flex items-center gap-2 px-3 py-2 hover:bg-white/5">
        <button
          onClick={() => setExpanded(!expanded)}
          className="flex items-center gap-2 flex-1 text-left cursor-pointer"
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
          <span className="font-medium text-text-primary">Event Timeline</span>
          {isStreaming && <Badge variant="blue" pulse>Ao vivo</Badge>}
          <Badge variant="gray">{events.length}</Badge>
          <span className="text-text-muted flex-shrink-0 ml-auto">{expanded ? '▾' : '▸'}</span>
        </button>
        {onClear && events.length > 0 && (
          <button
            onClick={(e) => {
              e.stopPropagation()
              onClear()
            }}
            className="text-[10px] text-text-muted hover:text-text-primary px-2 py-0.5 rounded border border-border-primary/50 hover:border-border-primary transition-colors"
            title="Limpa a timeline desta sessão"
          >
            Limpar
          </button>
        )}
      </div>

      {/* Corpo colapsável */}
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
            {displayEvents.map((evt) => {
              const runNum = runIndexByEventId.get(evt.id)
              return (
                <div key={evt.id}>
                  {/* Separador visual quando começa um novo run */}
                  {runNum !== undefined && (
                    <div className="flex items-center gap-2 px-3 py-1 bg-bg-primary/50 border-t border-border-primary text-text-dimmed">
                      <span className="text-[9px] font-mono uppercase tracking-wider">Run #{runNum}</span>
                      <span className="text-[9px] font-mono">{formatTime(evt.timestamp)}</span>
                      <span className="flex-1 h-px bg-border-primary" />
                    </div>
                  )}
                  <button
                    type="button"
                    onClick={() => setSelected(evt)}
                    className="w-full flex items-start gap-2 px-3 py-1 border-t border-border-primary first:border-t-0 hover:bg-white/5 text-left cursor-pointer"
                    title="Ver payload completo"
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
                  </button>
                </div>
              )
            })}
            {textContentCount > 0 && (
              <div className="px-3 py-1 border-t border-border-primary text-text-dimmed text-[10px]">
                + {textContentCount} TEXT_MESSAGE_CONTENT events (escondidos)
              </div>
            )}
          </div>
        </div>
      </div>

      {/* Modal com payload formatado */}
      <Modal
        open={selected !== null}
        onClose={() => setSelected(null)}
        title={selected ? `${selected.type} · ${formatTime(selected.timestamp)}` : ''}
        size="lg"
      >
        {selected && (
          <div className="flex flex-col gap-3">
            <div className="flex items-center gap-2 text-xs">
              <Badge variant={getTypeColor(selected.type)}>{selected.type}</Badge>
              <span className="text-text-muted font-mono">id={selected.id}</span>
              <span className="text-text-dimmed">·</span>
              <span className="text-text-muted font-mono">{formatTime(selected.timestamp)}</span>
            </div>
            <JsonViewer data={selected.data} collapsed={false} maxHeight="500px" />
          </div>
        )}
      </Modal>
    </div>
  )
}
