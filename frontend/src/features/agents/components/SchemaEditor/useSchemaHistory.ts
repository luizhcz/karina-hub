import { useState, useRef, useCallback } from 'react'

const MAX_HISTORY = 50

export function useSchemaHistory(initial: string) {
  const [state, setState] = useState(initial)
  const pastRef = useRef<string[]>([])
  const futureRef = useRef<string[]>([])

  const set = useCallback((next: string) => {
    setState((prev) => {
      pastRef.current = [...pastRef.current.slice(-(MAX_HISTORY - 1)), prev]
      futureRef.current = []
      return next
    })
  }, [])

  const undo = useCallback(() => {
    setState((prev) => {
      if (pastRef.current.length === 0) return prev
      const previous = pastRef.current[pastRef.current.length - 1]
      pastRef.current = pastRef.current.slice(0, -1)
      futureRef.current = [prev, ...futureRef.current]
      return previous
    })
  }, [])

  const redo = useCallback(() => {
    setState((prev) => {
      if (futureRef.current.length === 0) return prev
      const next = futureRef.current[0]
      futureRef.current = futureRef.current.slice(1)
      pastRef.current = [...pastRef.current, prev]
      return next
    })
  }, [])

  return {
    state,
    set,
    undo,
    redo,
    canUndo: pastRef.current.length > 0,
    canRedo: futureRef.current.length > 0,
  }
}
