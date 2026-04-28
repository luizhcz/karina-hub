import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useAgent } from '../../api/agents'
import {
  useRun,
  useRunResults,
  useCancelRun,
  exportRunUrl,
} from '../../api/evaluations'
import { Button } from '../../shared/ui/Button'
import { Card } from '../../shared/ui/Card'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { EvalScoreBadge } from './components/EvalScoreBadge'
import { TriggerSourceBadge } from './components/TriggerSourceBadge'
import { RunProgressStream } from './components/RunProgressStream'
import { SelfEnhancementBiasBanner } from './components/SelfEnhancementBiasBanner'

const STATUS_VARIANTS: Record<string, 'gray' | 'blue' | 'green' | 'red' | 'yellow'> = {
  Pending: 'gray',
  Running: 'blue',
  Completed: 'green',
  Failed: 'red',
  Cancelled: 'yellow',
}

export function EvaluationRunDetailPage() {
  const { runId } = useParams<{ runId: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { data: run, isLoading, error, refetch } = useRun(runId!, !!runId)
  const { data: agent } = useAgent(run?.agentDefinitionId ?? '', !!run?.agentDefinitionId)
  const [resultFilter, setResultFilter] = useState<{ passed?: boolean; evaluator?: string }>({})
  const { data: results } = useRunResults(runId!, resultFilter, !!runId)
  const cancelMutation = useCancelRun()

  if (isLoading) return <PageLoader />
  if (error || !run) return <ErrorCard message="Erro ao carregar run." onRetry={refetch} />

  const isLive = run.status === 'Running' || run.status === 'Pending'
  const isTerminal = run.status === 'Completed' || run.status === 'Failed' || run.status === 'Cancelled'
  // casesPassed/casesFailed contam results (avaliações individuais), não cases.
  const totalEvals = run.casesPassed + run.casesFailed
  const passRate = totalEvals > 0 ? run.casesPassed / totalEvals : null

  const judgeModel = results?.find((r) => r.judgeModel)?.judgeModel
  const evaluatorNames = Array.from(new Set(results?.map((r) => r.evaluatorName) ?? []))

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Link to={`/agents/${run.agentDefinitionId}`}>
          <Button variant="ghost" size="sm">&larr; Agente</Button>
        </Link>
        <div className="flex items-center gap-2 flex-1 min-w-0">
          <h1 className="text-xl font-bold text-text-primary font-mono truncate">{run.runId}</h1>
          <Badge variant={STATUS_VARIANTS[run.status] ?? 'gray'} pulse={isLive}>
            {run.status}
          </Badge>
          <TriggerSourceBadge source={run.triggerSource} />
        </div>
        {isTerminal && (
          <a href={exportRunUrl(runId!, 'csv')} target="_blank" rel="noopener noreferrer">
            <Button variant="secondary" size="sm">Export CSV</Button>
          </a>
        )}
        {run.baselineRunId && (
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/evaluations/compare?runA=${run.baselineRunId}&runB=${runId}`)}
          >
            Comparar com baseline
          </Button>
        )}
        {isLive && (
          <Button
            variant="ghost"
            size="sm"
            onClick={() => cancelMutation.mutate(runId!)}
            loading={cancelMutation.isPending}
          >
            Cancelar
          </Button>
        )}
      </div>

      <SelfEnhancementBiasBanner judgeModel={judgeModel} agentModel={agent?.model.deploymentName} />

      <Card>
        <div className="grid grid-cols-2 md:grid-cols-5 gap-3 text-sm">
          <Stat label="Cases" value={`${run.casesCompleted}/${run.casesTotal}`} />
          <Stat
            label="Avaliações"
            value={`${run.casesPassed} pass · ${run.casesFailed} fail`}
            subValue={totalEvals > 0 ? `${totalEvals} totais` : undefined}
          />
          <Stat
            label="Pass rate"
            value={passRate !== null ? `${(passRate * 100).toFixed(1)}%` : '—'}
            valueComponent={passRate !== null ? <EvalScoreBadge score={passRate} /> : undefined}
          />
          <Stat label="Cost" value={`$${run.totalCostUsd.toFixed(4)}`} />
          <Stat label="Tokens" value={run.totalTokens.toLocaleString('pt-BR')} />
        </div>

        {isLive && (
          <div className="mt-4">
            <RunProgressStream
              runId={runId!}
              onDone={() => {
                refetch()
                // Quando SSE termina, results ficam stale — invalida pra UI re-buscar.
                queryClient.invalidateQueries({ queryKey: ['run-results', runId] })
              }}
            />
          </div>
        )}

        {run.lastError && (
          <div className="mt-3 rounded-md border border-red-500/30 bg-red-500/10 px-3 py-2 text-xs text-red-300">
            <strong>Erro:</strong> {run.lastError}
          </div>
        )}

        <div className="mt-4 grid grid-cols-1 md:grid-cols-2 gap-2 text-xs text-text-muted">
          <div>AgentVersion: <code className="text-text-secondary">{run.agentVersionId.slice(0, 12)}…</code></div>
          <div>TestSetVersion: <code className="text-text-secondary">{run.testSetVersionId.slice(0, 12)}…</code></div>
          <div>EvaluatorConfigVersion: <code className="text-text-secondary">{run.evaluatorConfigVersionId.slice(0, 12)}…</code></div>
        </div>
      </Card>

      <Card>
        <div className="flex items-center gap-2 mb-3">
          <h3 className="text-lg font-semibold flex-1">Resultados</h3>
          <select
            value={resultFilter.passed === undefined ? '' : resultFilter.passed ? 'pass' : 'fail'}
            onChange={(e) => {
              const v = e.target.value
              setResultFilter({ ...resultFilter, passed: v === '' ? undefined : v === 'pass' })
            }}
            className="px-2 py-1 text-xs rounded bg-bg-secondary border border-border-secondary"
          >
            <option value="">Todos</option>
            <option value="pass">Apenas pass</option>
            <option value="fail">Apenas fail</option>
          </select>
          {evaluatorNames.length > 0 && (
            <select
              value={resultFilter.evaluator ?? ''}
              onChange={(e) =>
                setResultFilter({ ...resultFilter, evaluator: e.target.value || undefined })
              }
              className="px-2 py-1 text-xs rounded bg-bg-secondary border border-border-secondary"
            >
              <option value="">Todos evaluators</option>
              {evaluatorNames.map((n) => (
                <option key={n} value={n}>{n}</option>
              ))}
            </select>
          )}
        </div>

        {!results || results.length === 0 ? (
          <div className="text-sm text-text-muted py-4 text-center">
            Nenhum resultado ainda.
          </div>
        ) : (
          <div className="space-y-1.5 max-h-[600px] overflow-y-auto">
            {results.map((r) => (
              <div
                key={r.resultId}
                className="border border-border-secondary rounded-md px-3 py-2 hover:border-border-primary"
              >
                <div className="flex items-center gap-2 mb-1">
                  <EvalScoreBadge score={r.score ?? undefined} passed={r.passed} />
                  <span className="font-mono text-xs text-text-secondary">{r.evaluatorName}</span>
                  <span className="text-xs text-text-muted">case {r.caseId.slice(0, 8)}…</span>
                  {r.repetitionIndex > 0 && (
                    <Badge variant="gray">rep {r.repetitionIndex + 1}</Badge>
                  )}
                  {r.judgeModel && (
                    <span className="text-xs text-text-muted ml-auto font-mono">
                      judge: {r.judgeModel}
                    </span>
                  )}
                </div>
                {r.reason && <div className="text-xs text-text-secondary">{r.reason}</div>}
                {r.outputContent && (
                  <details className="mt-1">
                    <summary className="text-xs text-text-muted cursor-pointer hover:text-text-primary">
                      Output
                    </summary>
                    <pre className="mt-1 text-xs bg-bg-tertiary rounded p-2 whitespace-pre-wrap font-mono text-text-secondary">
                      {r.outputContent}
                    </pre>
                  </details>
                )}
              </div>
            ))}
          </div>
        )}
      </Card>
    </div>
  )
}

function Stat({
  label, value, subValue, valueComponent,
}: { label: string; value: string; subValue?: string; valueComponent?: React.ReactNode }) {
  return (
    <div className="border border-border-secondary rounded-md p-3">
      <div className="text-xs text-text-muted mb-1">{label}</div>
      <div className="font-mono text-sm text-text-primary">
        {valueComponent ?? value}
      </div>
      {subValue && (
        <div className="text-xs text-text-muted mt-0.5 font-mono">{subValue}</div>
      )}
    </div>
  )
}
