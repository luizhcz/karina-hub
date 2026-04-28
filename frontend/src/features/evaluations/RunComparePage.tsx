import { Link, useSearchParams } from 'react-router'
import { useCompareRuns } from '../../api/evaluations'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EvalScoreBadge } from './components/EvalScoreBadge'

export function RunComparePage() {
  const [params] = useSearchParams()
  const runA = params.get('runA') ?? undefined
  const runB = params.get('runB') ?? undefined
  const { data, isLoading, error, refetch } = useCompareRuns(runA, runB)

  if (!runA || !runB) {
    return (
      <ErrorCard message="Parâmetros runA e runB obrigatórios na URL." onRetry={refetch} />
    )
  }
  if (isLoading) return <PageLoader />
  if (error || !data) return <ErrorCard message="Erro ao comparar runs." onRetry={refetch} />

  const passRateDeltaPct = data.passRateDelta !== undefined && data.passRateDelta !== null
    ? (data.passRateDelta * 100).toFixed(1)
    : null

  const regressed = data.caseDiffs.filter((d) => d.passedA === true && d.passedB === false)
  const improved = data.caseDiffs.filter((d) => d.passedA === false && d.passedB === true)
  const unchanged = data.caseDiffs.filter((d) => d.passedA === d.passedB)

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Link to="/evaluations/test-sets">
          <Button variant="ghost" size="sm">&larr; Test Sets</Button>
        </Link>
        <h1 className="text-xl font-bold text-text-primary">Comparação de Runs</h1>
        {data.regressionDetected && (
          <Badge variant="red" pulse>⚠ Regressão detectada</Badge>
        )}
      </div>

      <Card>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <div className="text-xs text-text-muted mb-1">Run A (baseline)</div>
            <Link
              to={`/evaluations/runs/${data.runIdA}`}
              className="font-mono text-sm text-blue-400 hover:underline"
            >
              {data.runIdA.slice(0, 16)}…
            </Link>
          </div>
          <div>
            <div className="text-xs text-text-muted mb-1">Run B (comparado)</div>
            <Link
              to={`/evaluations/runs/${data.runIdB}`}
              className="font-mono text-sm text-blue-400 hover:underline"
            >
              {data.runIdB.slice(0, 16)}…
            </Link>
          </div>
        </div>

        <div className="grid grid-cols-3 gap-3 mt-4">
          <div className="border border-border-secondary rounded-md p-3 text-center">
            <div className="text-xs text-text-muted mb-1">Pass rate</div>
            <div className="font-mono text-sm text-text-primary">
              {data.passRateA !== undefined ? `${((data.passRateA ?? 0) * 100).toFixed(1)}%` : '—'}
              <span className="mx-2 text-text-muted">→</span>
              {data.passRateB !== undefined ? `${((data.passRateB ?? 0) * 100).toFixed(1)}%` : '—'}
            </div>
            {passRateDeltaPct !== null && (
              <div
                className={
                  'text-xs mt-1 font-mono ' +
                  (Number(passRateDeltaPct) < 0 ? 'text-red-400' : 'text-emerald-400')
                }
              >
                {Number(passRateDeltaPct) >= 0 ? '+' : ''}
                {passRateDeltaPct} pp
              </div>
            )}
          </div>
          <div className="border border-border-secondary rounded-md p-3 text-center">
            <div className="text-xs text-text-muted mb-1">Cases failed</div>
            <div className="font-mono text-sm text-text-primary">
              {data.casesFailedA} <span className="mx-2 text-text-muted">→</span> {data.casesFailedB}
            </div>
            <div
              className={
                'text-xs mt-1 font-mono ' +
                (data.casesFailedDelta > 0 ? 'text-red-400' : 'text-emerald-400')
              }
            >
              {data.casesFailedDelta > 0 ? '+' : ''}
              {data.casesFailedDelta}
            </div>
          </div>
          <div className="border border-border-secondary rounded-md p-3 text-center">
            <div className="text-xs text-text-muted mb-1">Total cases</div>
            <div className="font-mono text-sm text-text-primary">{data.caseDiffs.length}</div>
          </div>
        </div>

        <div className="grid grid-cols-3 gap-2 mt-4 text-xs">
          <Badge variant="red">Regressão: {regressed.length}</Badge>
          <Badge variant="green">Melhoria: {improved.length}</Badge>
          <Badge variant="gray">Sem mudança: {unchanged.length}</Badge>
        </div>
      </Card>

      <Card>
        <h3 className="text-lg font-semibold mb-3">Diffs por case</h3>
        <div className="space-y-1.5 max-h-[600px] overflow-y-auto">
          {[...regressed, ...improved, ...unchanged].map((d) => {
            const changed = d.passedA !== d.passedB
            return (
              <div
                key={d.caseId}
                className={
                  'border rounded-md px-3 py-2 ' +
                  (changed
                    ? d.passedA === true
                      ? 'border-red-500/30 bg-red-500/5'
                      : 'border-emerald-500/30 bg-emerald-500/5'
                    : 'border-border-secondary')
                }
              >
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-mono text-xs text-text-muted">case {d.caseId.slice(0, 8)}…</span>
                  <div className="flex items-center gap-1 ml-auto">
                    <EvalScoreBadge score={d.scoreA ?? undefined} passed={d.passedA ?? undefined} />
                    <span className="text-text-muted text-xs">→</span>
                    <EvalScoreBadge score={d.scoreB ?? undefined} passed={d.passedB ?? undefined} />
                  </div>
                </div>
                {(d.reasonA || d.reasonB) && (
                  <div className="grid grid-cols-2 gap-2 text-xs mt-1">
                    <div className="text-text-muted">{d.reasonA ?? '—'}</div>
                    <div className="text-text-muted">{d.reasonB ?? '—'}</div>
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </Card>
    </div>
  )
}
