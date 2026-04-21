import { useCallback } from 'react'
import { api } from '../api'
import type { NodeRecord, ExecutionEventRecord, WorkflowExecution } from '../types'

interface SSEHandlerOptions {
  execId: string | null
  setEvents: React.Dispatch<React.SetStateAction<{ type: string; payload: unknown; ts: number }[]>>
  setNodeStates: React.Dispatch<React.SetStateAction<Record<string, NodeRecord>>>
  setAuditEvents: React.Dispatch<React.SetStateAction<ExecutionEventRecord[]>>
  setExecution?: (exec: WorkflowExecution) => void
  /** Max events to keep in the live stream buffer */
  maxEvents?: number
  /** Called when workflow completes or errors */
  onComplete?: () => void
  /** Whether audit events should be appended (chat mode) or replaced (dashboard mode) */
  appendAudit?: boolean
}

/**
 * Creates a memoized SSE event handler that processes node_started/node_completed
 * and workflow_completed/error events. Eliminates duplication between dashboard
 * and chat SSE handling.
 */
export function useSSEEventHandler({
  execId,
  setEvents,
  setNodeStates,
  setAuditEvents,
  setExecution,
  maxEvents = 200,
  onComplete,
  appendAudit = false,
}: SSEHandlerOptions) {
  return useCallback((type: string, payload: unknown) => {
    const ts = Date.now()
    setEvents(prev => [...prev.slice(-maxEvents), { type, payload, ts }])

    if (type === 'node_started' || type === 'node_completed') {
      const p = payload as Record<string, unknown>
      const nodeId = p?.nodeId as string
      if (!nodeId) return
      setNodeStates(prev => ({
        ...prev,
        [nodeId]: {
          ...prev[nodeId],
          nodeId,
          executionId: execId ?? '',
          nodeType: (p?.nodeType as 'agent' | 'executor') ?? 'executor',
          status: type === 'node_started' ? 'running' : 'completed',
          startedAt: type === 'node_started'
            ? (p?.timestamp as string ?? new Date().toISOString())
            : prev[nodeId]?.startedAt,
          completedAt: type === 'node_completed'
            ? (p?.timestamp as string ?? new Date().toISOString())
            : undefined,
          output: type === 'node_completed' ? (p?.output as string) : prev[nodeId]?.output,
          iteration: prev[nodeId]?.iteration ?? 1,
        },
      }))
    }

    if (type === 'workflow_completed' || type === 'error') {
      if (execId) {
        if (setExecution) {
          api.getExecution(execId).then(setExecution).catch(console.error)
        }
        api.getNodes(execId).then(nodes => {
          const map: Record<string, NodeRecord> = {}
          nodes.forEach(n => { map[n.nodeId] = n })
          setNodeStates(map)
        }).catch(console.error)
        setTimeout(() => {
          api.getExecutionEvents(execId).then(evts => {
            if (appendAudit) {
              setAuditEvents(prev => [...prev, ...evts])
            } else {
              setAuditEvents(evts)
            }
          }).catch(console.error)
        }, 800)
      }
      onComplete?.()
    }
  }, [execId, setEvents, setNodeStates, setAuditEvents, setExecution, maxEvents, onComplete, appendAudit])
}
