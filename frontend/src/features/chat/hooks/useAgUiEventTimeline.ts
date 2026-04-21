import { useState, useCallback, useRef } from 'react'

export interface TimelineEvent {
  id: number
  type: string
  timestamp: number
  data: Record<string, unknown>
}

const MAX_EVENTS = 200

export function useAgUiEventTimeline() {
  const [events, setEvents] = useState<TimelineEvent[]>([])
  const nextId = useRef(0)

  const addEvent = useCallback((type: string, data: Record<string, unknown>) => {
    const evt: TimelineEvent = {
      id: nextId.current++,
      type,
      timestamp: Date.now(),
      data,
    }
    setEvents((prev) => {
      const next = [...prev, evt]
      return next.length > MAX_EVENTS ? next.slice(-MAX_EVENTS) : next
    })
  }, [])

  const clearEvents = useCallback(() => {
    setEvents([])
    nextId.current = 0
  }, [])

  return { events, addEvent, clearEvents }
}
