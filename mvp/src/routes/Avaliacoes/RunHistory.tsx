import { useEffect, useState } from 'react'
import { Badge, Card, ErrorMessage, Spinner, cn } from '../../ui'
import { friendlyError } from '../../api/client'
import { type EvalRunSummary, listEvalRunsByAgent } from '../../api/profileEvaluation'

interface RunHistoryProps {
  agentId: string | null
  agentName: string | null
  onSelect: (run: EvalRunSummary) => void
}

export function RunHistory({ agentId, agentName, onSelect }: RunHistoryProps) {
  const [runs, setRuns] = useState<EvalRunSummary[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!agentId) {
      setRuns([])
      return
    }
    let cancelled = false
    setLoading(true)
    setError(null)
    listEvalRunsByAgent(agentId, 50)
      .then((list) => {
        if (cancelled) return
        // Backend já ordena DESC por createdAt, mas garantimos por segurança.
        const sorted = [...list].sort((a, b) =>
          new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime(),
        )
        setRuns(sorted)
      })
      .catch((err) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar o histórico de avaliações.'))
      })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [agentId])

  if (!agentId) {
    return (
      <Card padded className="text-center">
        <p className="text-sm text-fg-muted">Selecione um agente à esquerda para ver o histórico de avaliações.</p>
      </Card>
    )
  }

  if (loading) {
    return (
      <Card className="flex items-center justify-center py-12">
        <Spinner className="h-5 w-5 text-fg-muted" />
      </Card>
    )
  }

  if (error) return <ErrorMessage message={error} />

  if (runs.length === 0) {
    return (
      <Card padded className="text-center">
        <h3 className="text-sm font-semibold text-fg">Sem avaliações ainda</h3>
        <p className="mt-1 text-xs text-fg-muted">
          {agentName ?? 'Este agente'} ainda não foi avaliado. Ao implantar uma versão, o auto-deploy dispara a primeira avaliação automaticamente.
        </p>
      </Card>
    )
  }

  return (
    <div className="space-y-2">
      {runs.map((run) => (
        <RunRow key={run.runId} run={run} onClick={() => onSelect(run)} />
      ))}
    </div>
  )
}

function RunRow({ run, onClick }: { run: EvalRunSummary; onClick: () => void }) {
  const preset = run.triggerContext?.preset ?? 'manual'
  const score = run.avgScore !== null && run.avgScore !== undefined
    ? Math.round(Number(run.avgScore) * 100)
    : null
  const failed = run.casesFailed ?? 0
  const passed = run.casesPassed ?? 0
  const total = run.casesTotal
  // Run completou mas nenhum evaluator emitiu Result (todos NotApplicable). Sinal
  // de que o preset não casa com o tipo de agente.
  const allSkipped = run.status === 'Completed' && total > 0 && passed + failed === 0

  return (
    <Card
      interactive
      padded
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onClick()
        }
      }}
      className="cursor-pointer"
    >
      <div className="flex flex-wrap items-center gap-3">
        <span className={cn('h-2 w-2 shrink-0 rounded-full', dotColor(run))} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-sm font-semibold text-fg">{formatDate(run.createdAt)}</span>
            <Badge tone="accent" className="text-[10px]">preset {preset}</Badge>
            <StatusBadge status={run.status} />
            {allSkipped && (
              <Badge tone="warning" className="text-[10px]">preset não aplicável</Badge>
            )}
          </div>
          <p className="mt-1 text-[11px] text-fg-muted">
            {allSkipped ? (
              <span>Todos os evaluators pularam — agente não casa com este preset.</span>
            ) : (
              <>
                {total} cases · {passed} OK · {failed} falhas
                {run.lastError && <span className="ml-2 text-danger">· {truncate(run.lastError, 80)}</span>}
              </>
            )}
          </p>
        </div>
        {allSkipped ? (
          <Badge tone="warning" className="text-[10px]">tente Média</Badge>
        ) : score !== null ? (
          <div className="text-right">
            <div className={cn(
              'text-2xl font-bold',
              score >= 80 ? 'text-success' : score >= 60 ? 'text-fg' : 'text-warning',
            )}>{score}</div>
            <div className="text-[10px] text-fg-dim">/100</div>
          </div>
        ) : (
          <Badge tone="neutral" className="text-[10px]">sem score</Badge>
        )}
      </div>
    </Card>
  )
}

function StatusBadge({ status }: { status: string }) {
  if (status === 'Completed') return <Badge tone="success" className="text-[10px]">Concluído</Badge>
  if (status === 'Running')   return <Badge tone="accent" className="text-[10px]">Rodando</Badge>
  if (status === 'Pending')   return <Badge tone="neutral" className="text-[10px]">Aguardando</Badge>
  if (status === 'Failed')    return <Badge tone="danger" className="text-[10px]">Falhou</Badge>
  if (status === 'Cancelled') return <Badge tone="neutral" className="text-[10px]">Cancelado</Badge>
  return <Badge tone="neutral" className="text-[10px]">{status}</Badge>
}

function dotColor(run: EvalRunSummary): string {
  if (run.status === 'Failed' || run.status === 'Cancelled') return 'bg-danger'
  if (run.status === 'Running' || run.status === 'Pending') return 'bg-accent'
  if (run.avgScore !== null && run.avgScore !== undefined) {
    const s = Number(run.avgScore) * 100
    if (s >= 80) return 'bg-success'
    if (s >= 60) return 'bg-fg-muted'
    return 'bg-warning'
  }
  return 'bg-fg-dim'
}

function formatDate(iso: string): string {
  const d = new Date(iso)
  return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}

function truncate(s: string, n: number): string {
  return s.length > n ? `${s.slice(0, n)}…` : s
}
