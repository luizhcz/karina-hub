import { useEffect, useRef, useState } from 'react'
import { useProjectStore } from '../../../stores/project'
import { streamRunUrl } from '../../../api/evaluations'
import type { RunStatus } from '../../../api/evaluations'

export interface RunProgressSnapshot {
  status: RunStatus
  casesTotal: number
  casesCompleted: number
  casesPassed: number
  casesFailed: number
  avgScore?: number
  totalCostUsd: number
  totalTokens: number
  lastError?: string
  startedAt?: string
  completedAt?: string
}

interface Props {
  runId: string
  onUpdate?: (snapshot: RunProgressSnapshot) => void
  onDone?: (snapshot: RunProgressSnapshot) => void
}

/** SSE wrapper para progress de eval run. */
export function RunProgressStream({ runId, onUpdate, onDone }: Props) {
  const projectId = useProjectStore((s) => s.projectId) ?? undefined
  const [snapshot, setSnapshot] = useState<RunProgressSnapshot | null>(null)
  const [error, setError] = useState<string | null>(null)
  const errorCountRef = useRef(0)

  useEffect(() => {
    const es = new EventSource(streamRunUrl(runId, projectId), { withCredentials: false })

    const handleProgress = (ev: MessageEvent) => {
      try {
        const data = JSON.parse(ev.data) as RunProgressSnapshot
        errorCountRef.current = 0
        setSnapshot(data)
        onUpdate?.(data)
      } catch (e) {
        console.warn('[RunProgressStream] Parse falhou:', e)
      }
    }

    const handleDone = (ev: MessageEvent) => {
      try {
        const data = JSON.parse(ev.data) as RunProgressSnapshot
        setSnapshot(data)
        onDone?.(data)
      } catch { /* ignore */ }
      es.close()
    }

    const handleError = () => {
      // EventSource reconecta sozinho; após 5 falhas seguidas, fecha pra evitar
      // loop infinito (run deletada / 404 / backend down).
      errorCountRef.current++
      if (errorCountRef.current >= 5) {
        setError('Stream perdeu conexão (timeout ou run inacessível).')
        es.close()
      }
    }

    es.addEventListener('progress', handleProgress)
    es.addEventListener('done', handleDone)
    es.addEventListener('error', handleError)

    return () => {
      es.removeEventListener('progress', handleProgress)
      es.removeEventListener('done', handleDone)
      es.removeEventListener('error', handleError)
      es.close()
    }
  }, [runId, projectId, onUpdate, onDone])

  if (error && !snapshot) {
    return <div className="text-sm text-red-400">{error}</div>
  }
  if (!snapshot) {
    return <div className="text-sm text-text-muted">Conectando ao stream…</div>
  }

  const pct = snapshot.casesTotal > 0
    ? Math.round((snapshot.casesCompleted / snapshot.casesTotal) * 100)
    : 0

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between text-sm">
        <span className="text-text-secondary">
          {snapshot.casesCompleted} / {snapshot.casesTotal} casos
        </span>
        <span className="font-mono text-text-muted">{pct}%</span>
      </div>
      <div className="w-full h-2 bg-bg-tertiary rounded-full overflow-hidden">
        <div
          className="h-full bg-blue-500 transition-all duration-300"
          style={{ width: `${pct}%` }}
        />
      </div>
      <div className="grid grid-cols-3 gap-2 text-xs text-text-muted">
        <div>✓ Pass: <span className="text-emerald-400 font-medium">{snapshot.casesPassed}</span></div>
        <div>✗ Fail: <span className="text-red-400 font-medium">{snapshot.casesFailed}</span></div>
        <div>$ Cost: <span className="text-text-secondary font-medium">${snapshot.totalCostUsd.toFixed(4)}</span></div>
      </div>
    </div>
  )
}
