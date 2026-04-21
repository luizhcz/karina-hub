import { useState, useRef, useCallback } from 'react'
import { applyPatch, type Operation } from 'fast-json-patch'

type AgentState = Record<string, Record<string, unknown>>

interface SharedStateRoot {
  agents?: AgentState
  [key: string]: unknown
}

export function useAgUiSharedState() {
  const [agentState, setAgentState] = useState<AgentState | null>(null)
  const [stateTimestamp, setStateTimestamp] = useState<string | null>(null)
  const [changedPaths, setChangedPaths] = useState<Set<string>>(new Set())
  const highlightTimeout = useRef<ReturnType<typeof setTimeout> | null>(null)
  // Keep a mutable ref of the full state root for applyPatch (avoids stale closure)
  const stateRootRef = useRef<SharedStateRoot>({})

  const handleStateSnapshot = useCallback((snapshot: unknown) => {
    if (!snapshot || typeof snapshot !== 'object') return
    const root = snapshot as SharedStateRoot
    stateRootRef.current = structuredClone(root)
    setAgentState(root.agents && Object.keys(root.agents).length > 0 ? { ...root.agents } : null)
    setStateTimestamp(new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }))
  }, [])

  const handleStateDelta = useCallback((delta: unknown) => {
    if (!Array.isArray(delta) || delta.length === 0) return

    try {
      const doc = structuredClone(stateRootRef.current)

      // Ensure intermediate parents exist for deep "add" operations.
      // The backend emits paths like "/agents/router-name" but the document
      // may be {} on the first message — fast-json-patch requires parents to exist.
      for (const op of delta as Operation[]) {
        if ((op.op === 'add' || op.op === 'replace') && op.path) {
          const segments = op.path.split('/').filter(Boolean)
          let cursor: Record<string, unknown> = doc as Record<string, unknown>
          for (let i = 0; i < segments.length - 1; i++) {
            if (!(segments[i] in cursor) || typeof cursor[segments[i]] !== 'object') {
              cursor[segments[i]] = {}
            }
            cursor = cursor[segments[i]] as Record<string, unknown>
          }
        }
      }

      const result = applyPatch(doc, delta as Operation[], true, false)
      const patched = result.newDocument as SharedStateRoot
      stateRootRef.current = patched

      // Extract changed paths for highlight animation
      const paths = new Set<string>()
      for (const op of delta as Operation[]) {
        // op.path example: "/agents/coletor-boleta/ticker"
        const segments = op.path.split('/').filter(Boolean)
        if (segments[0] === 'agents' && segments.length >= 2) {
          // Mark "agentKey.field" for highlight
          paths.add(segments.length >= 3 ? `${segments[1]}.${segments[2]}` : segments[1])
        }
      }

      setAgentState(patched.agents && Object.keys(patched.agents).length > 0 ? { ...patched.agents } : null)
      setStateTimestamp(new Date().toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }))

      if (paths.size > 0) {
        setChangedPaths(paths)
        if (highlightTimeout.current) clearTimeout(highlightTimeout.current)
        highlightTimeout.current = setTimeout(() => setChangedPaths(new Set()), 1500)
      }
    } catch (err) {
      console.warn('[AG-UI] Failed to apply state delta', err)
    }
  }, [])

  const clearState = useCallback(() => {
    stateRootRef.current = {}
    setAgentState(null)
    setStateTimestamp(null)
    setChangedPaths(new Set())
    if (highlightTimeout.current) {
      clearTimeout(highlightTimeout.current)
      highlightTimeout.current = null
    }
  }, [])

  return { agentState, stateTimestamp, changedPaths, handleStateSnapshot, handleStateDelta, clearState }
}
