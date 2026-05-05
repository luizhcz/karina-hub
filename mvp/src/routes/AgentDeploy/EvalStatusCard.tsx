import { useEffect, useRef, useState } from 'react'
import { Badge, Button, Card, CardHeader, Spinner, cn } from '../../ui'
import {
  type AutoDeployPreset,
  type EvalProgressEvent,
  type EvalRunSummary,
  getEvalRun,
  presetMeta,
  streamEvalRun,
} from '../../api/profileEvaluation'

interface EvalStatusCardProps {
  runId: string | null
  preset: AutoDeployPreset
  generatorFailed: boolean
  estimatedCostUsd: number
  caseCount: number
  onRetry?: () => void
}

const SOFT_GATE_THRESHOLD = 0.6

export function EvalStatusCard({
  runId,
  preset,
  generatorFailed,
  estimatedCostUsd,
  caseCount,
  onRetry,
}: EvalStatusCardProps) {
  const [progress, setProgress] = useState<EvalProgressEvent | null>(null)
  const [streamError, setStreamError] = useState<string | null>(null)
  const [pollFallback, setPollFallback] = useState(false)
  const closerRef = useRef<(() => void) | null>(null)
  const pollRef = useRef<number | null>(null)

  const meta = presetMeta(preset)

  useEffect(() => {
    if (!runId) return
    let cancelled = false

    // Snapshot inicial via GET — cobre F5 quando run já existe.
    getEvalRun(runId).then((run) => {
      if (cancelled) return
      setProgress(toProgress(run))
    }).catch(() => { /* SSE cobre */ })

    // Stream SSE.
    const closer = streamEvalRun(
      runId,
      (e) => { if (!cancelled) setProgress(e) },
      (e) => { if (!cancelled) setProgress(e) },
      () => {
        if (!cancelled) {
          setStreamError('SSE indisponível — usando polling.')
          setPollFallback(true)
        }
      },
    )
    closerRef.current = closer

    return () => {
      cancelled = true
      closerRef.current?.()
      if (pollRef.current !== null) window.clearInterval(pollRef.current)
    }
  }, [runId])

  // Poll fallback quando SSE caiu.
  useEffect(() => {
    if (!pollFallback || !runId) return
    pollRef.current = window.setInterval(async () => {
      try {
        const run = await getEvalRun(runId)
        const ev = toProgress(run)
        setProgress(ev)
        if (isTerminal(ev.status) && pollRef.current !== null) {
          window.clearInterval(pollRef.current)
          pollRef.current = null
        }
      } catch {
        // ignore — silently retries
      }
    }, 5000)
    return () => {
      if (pollRef.current !== null) {
        window.clearInterval(pollRef.current)
        pollRef.current = null
      }
    }
  }, [pollFallback, runId])

  if (generatorFailed) {
    return (
      <Card className="space-y-3">
        <CardHeader
          title="Avaliação não disponível"
          description="O gerador de test cases não conseguiu produzir uma suíte agora. O deploy do workflow continua válido."
        />
        {onRetry && (
          <Button size="sm" variant="secondary" onClick={onRetry}>Tentar novamente</Button>
        )}
      </Card>
    )
  }

  if (!runId) return null

  const status = progress?.status ?? 'Pending'
  const completed = progress?.casesCompleted ?? 0
  const total = progress?.casesTotal ?? caseCount
  const passed = progress?.casesPassed ?? 0
  const failed = progress?.casesFailed ?? 0
  const score = progress?.avgScore ?? null
  const cost = progress?.totalCostUsd ?? null
  const pct = total > 0 ? Math.round((completed / total) * 100) : 0
  const lowScore = isTerminal(status) && score !== null && Number(score) < SOFT_GATE_THRESHOLD
  // Run terminou mas nenhum evaluator emitiu Result — todos pularam (NotApplicable).
  // Acontece quando o preset escolhido não casa com o tipo de agente (ex.: Basic
  // contra agente de chat puro, sem tools nem ExpectedOutput literal).
  const allSkipped = isTerminal(status) && total > 0 && passed + failed === 0

  return (
    <Card className="space-y-3">
      <CardHeader
        title="Status da avaliação"
        description={`Preset ${meta.label} · ${total} cases · custo estimado ${meta.cost}`}
        actions={<StatusBadge status={status} />}
      />

      {/* Barra de progresso */}
      <div className="space-y-1">
        <div className="flex items-center justify-between text-[11px] text-fg-muted">
          <span>{completed}/{total} cases</span>
          <span>{pct}%</span>
        </div>
        <div className="h-2 w-full overflow-hidden rounded-full bg-bg-soft">
          <div
            className={cn(
              'h-full transition-all duration-500',
              isTerminal(status) ? (lowScore ? 'bg-warning' : 'bg-success') : 'bg-accent',
            )}
            style={{ width: `${pct}%` }}
          />
        </div>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-3 gap-3 text-center">
        <Stat label="Passed" value={String(passed)} tone="success" />
        <Stat label="Failed" value={String(failed)} tone={failed > 0 ? 'danger' : 'neutral'} />
        <Stat
          label="Score"
          value={score !== null ? formatScore(score) : '—'}
          tone={lowScore ? 'warning' : 'neutral'}
        />
      </div>

      {/* Banner: preset não aplicável (todos os evaluators pularam) */}
      {allSkipped && (
        <div className="rounded-md border border-warning/40 bg-warning/10 px-3 py-2 text-xs text-warning">
          <p className="font-semibold">Preset {meta.label} não é aplicável a este agente.</p>
          <p className="mt-1 text-fg-muted">
            Os evaluators Local (ContainsExpected, ToolCalledCheck) só funcionam com agentes que tenham tools
            ou output literal previsível. Em agentes de chat livre, prefira o preset <strong>Média</strong> —
            avalia a resposta semanticamente.
          </p>
        </div>
      )}

      {/* Soft warning */}
      {lowScore && !allSkipped && (
        <p className="rounded-md bg-warning/10 px-3 py-2 text-xs text-warning">
          Score abaixo do esperado — revise os resultados antes de habilitar consumo externo.
        </p>
      )}

      {/* Streaming hint */}
      {!isTerminal(status) && (
        <div className="flex items-center gap-2 text-[11px] text-fg-dim">
          <Spinner className="h-3 w-3" />
          <span>{streamError ?? 'Avaliando em tempo real…'}</span>
        </div>
      )}

      {/* Cost actual */}
      {cost !== null && Number(cost) > 0 && (
        <p className="text-[11px] text-fg-dim">Custo real: ${Number(cost).toFixed(4)} USD</p>
      )}

      {estimatedCostUsd === 0 && progress === null && (
        <p className="text-[11px] text-fg-dim">Sem custo de LLM — preset Local.</p>
      )}
    </Card>
  )
}

interface StatProps {
  label: string
  value: string
  tone: 'success' | 'danger' | 'neutral' | 'warning'
}

function Stat({ label, value, tone }: StatProps) {
  const toneClass = {
    success: 'text-success',
    danger: 'text-danger',
    warning: 'text-warning',
    neutral: 'text-fg',
  }[tone]
  return (
    <div className="rounded-lg border border-border bg-bg-soft px-3 py-2">
      <div className={cn('text-base font-semibold', toneClass)}>{value}</div>
      <div className="text-[10px] uppercase tracking-wider text-fg-dim">{label}</div>
    </div>
  )
}

function StatusBadge({ status }: { status: string }) {
  if (status === 'Completed') return <Badge tone="success">Concluído</Badge>
  if (status === 'Running')   return <Badge tone="accent">Rodando</Badge>
  if (status === 'Pending')   return <Badge tone="neutral">Aguardando</Badge>
  if (status === 'Failed')    return <Badge tone="danger">Falhou</Badge>
  if (status === 'Cancelled') return <Badge tone="neutral">Cancelado</Badge>
  return <Badge tone="neutral">{status}</Badge>
}

function toProgress(run: EvalRunSummary): EvalProgressEvent {
  return {
    status: run.status,
    casesTotal: run.casesTotal,
    casesCompleted: run.casesCompleted ?? 0,
    casesPassed: run.casesPassed ?? 0,
    casesFailed: run.casesFailed ?? 0,
    avgScore: (run.avgScore ?? null) as number | null,
    totalCostUsd: 0,
    totalTokens: 0,
    lastError: run.lastError ?? null,
    startedAt: run.startedAt ?? null,
    completedAt: run.completedAt ?? null,
  }
}

function isTerminal(status: string): boolean {
  return status === 'Completed' || status === 'Failed' || status === 'Cancelled'
}

function formatScore(score: number | string): string {
  const n = typeof score === 'string' ? parseFloat(score) : score
  if (Number.isNaN(n)) return '—'
  return Math.round(n * 100).toString()
}
