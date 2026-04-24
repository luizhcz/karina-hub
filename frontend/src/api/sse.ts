import { useRef, useState, useCallback, useEffect } from 'react'
import { apiUrl } from './client'


export interface SSEEvent {
  type: string
  payload: Record<string, unknown>
}

export interface SSEHookOptions {
  onEvent?: (event: SSEEvent) => void
  onError?: (error: Event) => void
  onOpen?: () => void
  autoConnect?: boolean
}

export interface SSEHookReturn {
  connect: () => void
  disconnect: () => void
  isConnected: boolean
}


export function useExecutionSSE(executionId: string | undefined, options: SSEHookOptions = {}): SSEHookReturn {
  const { onEvent, onError, onOpen, autoConnect = false } = options
  const sourceRef = useRef<EventSource | null>(null)
  const [isConnected, setIsConnected] = useState(false)

  const disconnect = useCallback(() => {
    if (sourceRef.current) {
      sourceRef.current.close()
      sourceRef.current = null
      setIsConnected(false)
    }
  }, [])

  const connect = useCallback(() => {
    if (!executionId) return
    disconnect()

    const es = new EventSource(apiUrl(`/executions/${executionId}/stream`))
    sourceRef.current = es

    es.onopen = () => {
      setIsConnected(true)
      onOpen?.()
    }

    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data) as SSEEvent
        onEvent?.(data)
      } catch {
        onEvent?.({ type: e.type || 'message', payload: { raw: e.data } })
      }
    }

    es.onerror = (e) => {
      setIsConnected(false)
      onError?.(e)
    }
  }, [executionId, disconnect, onEvent, onError, onOpen])

  useEffect(() => {
    if (autoConnect && executionId) {
      connect()
    }
    return disconnect
  }, [autoConnect, executionId, connect, disconnect])

  return { connect, disconnect, isConnected }
}


export function useChatSSE(conversationId: string | undefined, options: SSEHookOptions = {}): SSEHookReturn {
  const { onEvent, onError, onOpen, autoConnect = false } = options
  const sourceRef = useRef<EventSource | null>(null)
  const [isConnected, setIsConnected] = useState(false)

  const disconnect = useCallback(() => {
    if (sourceRef.current) {
      sourceRef.current.close()
      sourceRef.current = null
      setIsConnected(false)
    }
  }, [])

  const connect = useCallback(() => {
    if (!conversationId) return
    disconnect()

    const es = new EventSource(apiUrl(`/conversations/${conversationId}/messages/stream`))
    sourceRef.current = es

    es.onopen = () => {
      setIsConnected(true)
      onOpen?.()
    }

    es.onmessage = (e) => {
      try {
        const data = JSON.parse(e.data) as SSEEvent
        onEvent?.(data)
      } catch {
        onEvent?.({ type: e.type || 'message', payload: { raw: e.data } })
      }
    }

    es.onerror = (e) => {
      setIsConnected(false)
      onError?.(e)
    }
  }, [conversationId, disconnect, onEvent, onError, onOpen])

  useEffect(() => {
    if (autoConnect && conversationId) {
      connect()
    }
    return disconnect
  }, [autoConnect, conversationId, connect, disconnect])

  return { connect, disconnect, isConnected }
}
