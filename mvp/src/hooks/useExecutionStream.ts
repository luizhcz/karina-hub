import { useEffect, useRef } from 'react'
import { getIdentity } from '../stores/identity'
import {
  STREAM_EVENT_TYPES,
  executionStreamUrl,
  type StreamEventType,
  type WorkflowEvent,
} from '../api/workflows'

export interface UseExecutionStreamOptions {
  /** ID da execução. `null` desativa o stream (útil enquanto o trigger ainda
   *  não retornou). */
  executionId: string | null
  /** Callback chamado pra cada evento parseado. Identidade do callback deve
   *  ser estável (use `useCallback` no consumidor). */
  onEvent: (event: WorkflowEvent) => void
  /** Callback opcional pra falhas de conexão / parse. */
  onError?: (error: unknown) => void
}

/**
 * Abre um <c>EventSource</c> pro endpoint SSE de execução e despacha eventos
 * parseados. Fecha a conexão automaticamente quando o componente desmonta ou
 * quando o <c>executionId</c> muda.
 *
 * O backend encerra o stream após um terminal event (workflow_completed,
 * workflow_failed, workflow_cancelled, error) — quando isso acontece o
 * EventSource entra em estado closed e o hook não tenta reconectar.
 */
export function useExecutionStream({ executionId, onEvent, onError }: UseExecutionStreamOptions) {
  // Mantém callbacks em refs pra que o effect só dependa do executionId.
  // Evita reabrir o stream quando o consumidor recria a função inline.
  const onEventRef = useRef(onEvent)
  const onErrorRef = useRef(onError)

  useEffect(() => {
    onEventRef.current = onEvent
    onErrorRef.current = onError
  })

  useEffect(() => {
    if (!executionId) return undefined

    const account = getIdentity()?.account
    if (!account) {
      onErrorRef.current?.(new Error('Identidade ausente — defina conta antes de abrir o stream.'))
      return undefined
    }

    const url = executionStreamUrl(executionId, account)
    const source = new EventSource(url)

    const dispatch = (eventType: string) => (raw: MessageEvent<string>) => {
      let payload: unknown = null
      try {
        payload = raw.data ? JSON.parse(raw.data) : null
      } catch (err) {
        onErrorRef.current?.(err)
        return
      }
      onEventRef.current({ type: eventType as StreamEventType, payload } as WorkflowEvent)
    }

    for (const type of STREAM_EVENT_TYPES) {
      source.addEventListener(type, dispatch(type) as EventListener)
    }

    source.onerror = (event) => {
      // EventSource fecha sozinho com readyState=2 quando o servidor encerra
      // o stream em terminal event. Não logamos erro nesse caso esperado.
      if (source.readyState === EventSource.CLOSED) return
      onErrorRef.current?.(event)
    }

    return () => {
      source.close()
    }
  }, [executionId])
}
