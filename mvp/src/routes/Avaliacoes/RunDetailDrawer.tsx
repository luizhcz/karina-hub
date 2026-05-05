import { useEffect, useMemo, useState } from 'react'
import {
  Badge,
  Button,
  CloseIcon,
  ErrorMessage,
  IconButton,
  SparklesIcon,
  Spinner,
  cn,
} from '../../ui'
import {
  type EvalResultDetail,
  type EvalRunSummary,
  listResultsByRun,
} from '../../api/profileEvaluation'
import { friendlyError } from '../../api/client'
import { evaluatorLabel, evaluatorShort, getEvaluatorMeta } from './evaluatorMeta'

interface RunDetailDrawerProps {
  open: boolean
  run: EvalRunSummary | null
  onClose: () => void
  onOpenGlossary: (highlightedNames: string[]) => void
}

export function RunDetailDrawer({ open, run, onClose, onOpenGlossary }: RunDetailDrawerProps) {
  const [results, setResults] = useState<EvalResultDetail[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [filter, setFilter] = useState<'all' | 'passed' | 'failed'>('all')

  useEffect(() => {
    if (!open || !run) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, run, onClose])

  useEffect(() => {
    if (!open || !run) return
    let cancelled = false
    setLoading(true)
    setError(null)
    listResultsByRun(run.runId)
      .then((list) => { if (!cancelled) setResults(list) })
      .catch((err) => { if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar os resultados.')) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [open, run])

  const evaluatorNames = useMemo(
    () => Array.from(new Set(results.map((r) => r.evaluatorName))),
    [results],
  )

  const filteredResults = useMemo(() => {
    if (filter === 'all') return results
    if (filter === 'passed') return results.filter((r) => r.passed)
    return results.filter((r) => !r.passed)
  }, [results, filter])

  // Agrupa por caseId — cada case pode ter múltiplos results (1 por evaluator × repetição).
  const byCase = useMemo(() => {
    const map = new Map<string, EvalResultDetail[]>()
    for (const r of filteredResults) {
      const list = map.get(r.caseId) ?? []
      list.push(r)
      map.set(r.caseId, list)
    }
    return Array.from(map.entries())
  }, [filteredResults])

  if (!open || !run) return null

  const preset = run.triggerContext?.preset ?? 'manual'
  const score = run.avgScore !== null && run.avgScore !== undefined
    ? Math.round(Number(run.avgScore) * 100)
    : null

  const passedCount = results.filter((r) => r.passed).length
  const failedCount = results.filter((r) => !r.passed).length

  return (
    <div className="fixed inset-0 z-40 flex" role="dialog" aria-label="Detalhes da avaliação">
      <div className="flex-1 bg-fg/20 backdrop-blur-[1px]" onClick={onClose} />
      <aside className="flex h-full w-[640px] max-w-[100vw] flex-col border-l border-border bg-surface shadow-2xl">
        <header className="flex shrink-0 items-start justify-between gap-3 border-b border-border px-4 py-3">
          <div className="min-w-0">
            <p className="text-[10px] font-semibold uppercase tracking-widest text-fg-dim">Avaliação</p>
            <h2 className="mt-0.5 text-base font-semibold text-fg">{formatRunHeader(run)}</h2>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <Badge tone="accent" className="text-[10px]">preset {preset}</Badge>
              <span className="text-[11px] text-fg-dim">{run.casesTotal} cases · {results.length} avaliações</span>
              {score !== null && (
                <Badge tone={score >= 60 ? 'success' : 'warning'} className="text-[10px]">Score {score}/100</Badge>
              )}
            </div>
          </div>
          <IconButton aria-label="Fechar" onClick={onClose}>
            <CloseIcon className="h-4 w-4" />
          </IconButton>
        </header>

        {/* Filtros + glossário */}
        <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-b border-border px-4 py-2.5">
          <div className="flex flex-wrap gap-1.5">
            {(['all', 'failed', 'passed'] as const).map((k) => {
              const active = filter === k
              const label = k === 'all' ? `Todas (${results.length})` : k === 'failed' ? `Falhas (${failedCount})` : `OK (${passedCount})`
              return (
                <button
                  key={k}
                  onClick={() => setFilter(k)}
                  className={cn(
                    'rounded-full border px-2.5 py-1 text-[11px] font-medium transition',
                    active
                      ? 'border-accent bg-accent-subtle text-accent'
                      : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg',
                  )}
                >
                  {label}
                </button>
              )
            })}
          </div>
          <Button
            size="sm"
            variant="ghost"
            leftIcon={<SparklesIcon className="h-3.5 w-3.5" />}
            onClick={() => onOpenGlossary(evaluatorNames)}
          >
            Glossário
          </Button>
        </div>

        <div className="flex-1 overflow-y-auto">
          {loading && (
            <div className="flex items-center justify-center py-10">
              <Spinner className="h-5 w-5 text-fg-muted" />
            </div>
          )}
          {!loading && error && <div className="p-4"><ErrorMessage message={error} /></div>}
          {!loading && !error && byCase.length === 0 && (
            <div className="px-4 py-10 text-center text-sm text-fg-muted">
              Sem avaliações para o filtro escolhido.
            </div>
          )}
          {!loading && !error && byCase.map(([caseId, items]) => (
            <CaseCard key={caseId} caseId={caseId} items={items} />
          ))}
        </div>
      </aside>
    </div>
  )
}

function CaseCard({ caseId, items }: { caseId: string; items: EvalResultDetail[] }) {
  const [open, setOpen] = useState(false)
  const passedCount = items.filter((r) => r.passed).length
  const totalCount = items.length
  const allPassed = passedCount === totalCount
  const noPassed = passedCount === 0

  return (
    <article className="border-b border-border">
      <button
        onClick={() => setOpen((v) => !v)}
        className="flex w-full items-center gap-3 px-4 py-3 text-left transition hover:bg-surface-hover"
      >
        <span
          className={cn(
            'h-2 w-2 shrink-0 rounded-full',
            allPassed ? 'bg-success' : noPassed ? 'bg-danger' : 'bg-warning',
          )}
        />
        <div className="min-w-0 flex-1">
          <p className="font-mono text-[10px] uppercase tracking-wider text-fg-dim">case {caseId.slice(0, 8)}</p>
          <p className="mt-0.5 truncate text-sm text-fg">
            {passedCount}/{totalCount} avaliações OK
          </p>
        </div>
        <span className={cn('text-fg-dim transition-transform', open && 'rotate-90')}>›</span>
      </button>
      {open && (
        <div className="space-y-2 px-4 pb-3">
          {items.map((r) => (
            <ResultRow key={r.resultId} result={r} />
          ))}
        </div>
      )}
    </article>
  )
}

function ResultRow({ result }: { result: EvalResultDetail }) {
  const meta = getEvaluatorMeta(result.evaluatorName)
  const score = result.score !== null ? Math.round(Number(result.score) * 100) : null

  return (
    <div
      className={cn(
        'rounded-lg border bg-surface p-3',
        result.passed ? 'border-success/30 bg-success/5' : 'border-danger/30 bg-danger/5',
      )}
    >
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone={meta?.kind === 'Meai' ? 'accent' : 'neutral'} className="text-[10px]">
          {meta?.kind ?? '?'}
        </Badge>
        <span
          className="text-sm font-semibold text-fg"
          title={evaluatorShort(result.evaluatorName)}
        >
          {evaluatorLabel(result.evaluatorName)}
        </span>
        {score !== null && (
          <Badge tone={result.passed ? 'success' : 'warning'} className="text-[10px]">
            {score}/100
          </Badge>
        )}
        <span className="text-[10px] text-fg-dim">{result.passed ? 'OK' : 'falhou'}</span>
        {result.repetitionIndex > 0 && (
          <span className="text-[10px] text-fg-dim">rep #{result.repetitionIndex}</span>
        )}
      </div>
      {result.reason && (
        <p className="mt-1.5 text-xs leading-relaxed text-fg-muted">{result.reason}</p>
      )}
      {result.judgeModel && (
        <p className="mt-1 font-mono text-[10px] text-fg-dim">judge: {result.judgeModel}</p>
      )}
    </div>
  )
}

function formatRunHeader(run: EvalRunSummary): string {
  if (!run.startedAt) return `Run · ${run.runId.slice(0, 8)}`
  const d = new Date(run.startedAt)
  return d.toLocaleString('pt-BR', { dateStyle: 'short', timeStyle: 'short' })
}
