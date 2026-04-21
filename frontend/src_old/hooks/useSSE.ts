import { useEffect, useRef, useCallback } from 'react'

export type SSEHandler = (eventType: string, payload: unknown) => void

export function useSSE(executionId: string | null, onEvent: SSEHandler) {
  const handlerRef = useRef(onEvent)
  handlerRef.current = onEvent

  const stopRef = useRef(false)

  const stop = useCallback(() => { stopRef.current = true }, [])

  useEffect(() => {
    if (!executionId) return
    stopRef.current = false

    const es = new EventSource(`/api/executions/${executionId}/stream`)

    const handleMsg = (e: MessageEvent, type: string) => {
      if (stopRef.current) return
      try {
        const payload = JSON.parse(e.data)
        handlerRef.current(type, payload)
      } catch {
        handlerRef.current(type, e.data)
      }
    }

    const eventTypes = ['token', 'node_started', 'node_completed', 'step_completed',
      'workflow_completed', 'error', 'hitl_required']

    eventTypes.forEach(type => {
      es.addEventListener(type, (e) => {
        handleMsg(e as MessageEvent, type)
        if (type === 'workflow_completed' || type === 'error') {
          stopRef.current = true
          es.close()
        }
      })
    })

    es.onerror = () => {
      if (!stopRef.current) es.close()
    }

    return () => {
      stopRef.current = true
      es.close()
    }
  }, [executionId])

  return { stop }
}
