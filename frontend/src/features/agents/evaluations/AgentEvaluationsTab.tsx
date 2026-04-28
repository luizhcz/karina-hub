import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useProjectStore } from '../../../stores/project'
import {
  useAgentRuns,
  useEvaluatorConfig,
  useTestSets,
  useEnqueueRun,
} from '../../../api/evaluations'
import type { EvaluationRun, TriggerSource } from '../../../api/evaluations'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'
import { Modal } from '../../../shared/ui/Modal'
import { EmptyState } from '../../../shared/ui/EmptyState'
import { EvalScoreBadge } from '../../evaluations/components/EvalScoreBadge'
import { TriggerSourceBadge } from '../../evaluations/components/TriggerSourceBadge'
import { QueueDepthIndicator } from '../../evaluations/components/QueueDepthIndicator'
import { RegressionConfigPanel } from './RegressionConfigPanel'
import { EvaluatorConfigEditor } from './EvaluatorConfigEditor'

interface Props {
  agentId: string
}

const STATUS_VARIANTS: Record<EvaluationRun['status'], 'gray' | 'blue' | 'green' | 'red' | 'yellow'> = {
  Pending: 'gray',
  Running: 'blue',
  Completed: 'green',
  Failed: 'red',
  Cancelled: 'yellow',
}

export function AgentEvaluationsTab({ agentId }: Props) {
  const navigate = useNavigate()
  const projectId = useProjectStore((s) => s.projectId) ?? 'default'
  const [filterSource, setFilterSource] = useState<TriggerSource | undefined>(undefined)
  const { data: runs, isLoading } = useAgentRuns(agentId, { take: 50, triggerSource: filterSource })
  const { data: evaluatorConfig } = useEvaluatorConfig(agentId)
  const { data: testSets } = useTestSets(projectId)
  const enqueueMutation = useEnqueueRun()

  const [enqueueOpen, setEnqueueOpen] = useState(false)
  const [pickedTestSetId, setPickedTestSetId] = useState('')
  const [activeSubTab, setActiveSubTab] = useState<'runs' | 'config' | 'regression'>('runs')

  const evaluatorVersionId = evaluatorConfig?.currentVersion?.evaluatorConfigVersionId
  const canEnqueue = !!evaluatorVersionId && !!testSets && testSets.length > 0

  const pickedTestSet = useMemo(
    () => testSets?.find((ts) => ts.id === pickedTestSetId),
    [testSets, pickedTestSetId],
  )

  const handleEnqueue = async () => {
    if (!evaluatorVersionId || !pickedTestSet?.currentVersionId) return
    const result = await enqueueMutation.mutateAsync({
      agentId,
      body: {
        testSetVersionId: pickedTestSet.currentVersionId,
        evaluatorConfigVersionId: evaluatorVersionId,
      },
    })
    setEnqueueOpen(false)
    setPickedTestSetId('')
    if (result.runId) navigate(`/evaluations/runs/${result.runId}`)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2 border-b border-border-secondary">
        {(['runs', 'config', 'regression'] as const).map((tab) => (
          <button
            key={tab}
            onClick={() => setActiveSubTab(tab)}
            className={
              'px-4 py-2 text-sm font-medium border-b-2 -mb-px ' +
              (activeSubTab === tab
                ? 'border-blue-500 text-blue-400'
                : 'border-transparent text-text-muted hover:text-text-primary')
            }
          >
            {tab === 'runs' ? 'Runs' : tab === 'config' ? 'Evaluator Config' : 'Regression'}
          </button>
        ))}
      </div>

      {activeSubTab === 'runs' && (
        <>
          <div className="flex items-center justify-between">
            <div className="flex items-center gap-3">
              <select
                value={filterSource ?? ''}
                onChange={(e) =>
                  setFilterSource((e.target.value || undefined) as TriggerSource | undefined)
                }
                className="px-3 py-1.5 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
              >
                <option value="">Todos triggers</option>
                <option value="Manual">Manual</option>
                <option value="AgentVersionPublished">Auto (publish)</option>
                <option value="ApiClient">API</option>
              </select>
              <QueueDepthIndicator agentId={agentId} />
            </div>
            <Button variant="primary" onClick={() => setEnqueueOpen(true)} disabled={!canEnqueue}>
              ▶ Run evaluation
            </Button>
          </div>

          {!canEnqueue && (
            <Card className="border-amber-500/30 bg-amber-500/5">
              <div className="text-sm text-amber-300">
                Para enfileirar runs, configure um <strong>EvaluatorConfig</strong> (aba Evaluator Config)
                e crie pelo menos um <strong>Test Set</strong> publicado.
              </div>
            </Card>
          )}

          {isLoading ? (
            <div className="text-sm text-text-muted">Carregando runs…</div>
          ) : !runs || runs.length === 0 ? (
            <EmptyState
              title="Nenhuma run ainda"
              description="Configure regression baseline para autotrigger ou rode manualmente."
            />
          ) : (
            <Card className="p-0 overflow-hidden">
              <table className="w-full text-sm">
                <thead className="bg-bg-tertiary border-b border-border-secondary">
                  <tr className="text-text-muted text-xs uppercase">
                    <th className="px-3 py-2 text-left">Status</th>
                    <th className="px-3 py-2 text-left">Trigger</th>
                    <th className="px-3 py-2 text-left">Cases</th>
                    <th className="px-3 py-2 text-left">Avaliações</th>
                    <th className="px-3 py-2 text-left">Pass rate</th>
                    <th className="px-3 py-2 text-left">Cost</th>
                    <th className="px-3 py-2 text-left">Iniciado</th>
                  </tr>
                </thead>
                <tbody>
                  {runs.map((r) => {
                    // casesPassed/casesFailed contam results individuais, não cases distintos.
                    // Cases distintos = casesCompleted.
                    const totalEvals = r.casesPassed + r.casesFailed
                    const passRate = totalEvals > 0 ? r.casesPassed / totalEvals : null
                    return (
                      <tr
                        key={r.runId}
                        className="border-b border-border-secondary hover:bg-bg-tertiary/40 cursor-pointer"
                        onClick={() => navigate(`/evaluations/runs/${r.runId}`)}
                      >
                        <td className="px-3 py-2">
                          <Badge variant={STATUS_VARIANTS[r.status]} pulse={r.status === 'Running'}>
                            {r.status}
                          </Badge>
                        </td>
                        <td className="px-3 py-2"><TriggerSourceBadge source={r.triggerSource} /></td>
                        <td className="px-3 py-2 text-text-secondary">
                          {r.casesCompleted}/{r.casesTotal}
                        </td>
                        <td className="px-3 py-2 font-mono text-xs text-text-muted">
                          {r.casesPassed}+{r.casesFailed} = {totalEvals}
                        </td>
                        <td className="px-3 py-2"><EvalScoreBadge score={passRate ?? undefined} /></td>
                        <td className="px-3 py-2 font-mono text-xs text-text-muted">
                          ${r.totalCostUsd.toFixed(4)}
                        </td>
                        <td className="px-3 py-2 text-xs text-text-muted">
                          {r.startedAt ? new Date(r.startedAt).toLocaleString('pt-BR') : '—'}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </Card>
          )}
        </>
      )}

      {activeSubTab === 'config' && <EvaluatorConfigEditor agentId={agentId} />}
      {activeSubTab === 'regression' && <RegressionConfigPanel agentId={agentId} />}

      <Modal open={enqueueOpen} onClose={() => setEnqueueOpen(false)} title="Run evaluation">
        <div className="space-y-3">
          <div>
            <label className="block text-xs text-text-muted mb-1">Test Set</label>
            <select
              value={pickedTestSetId}
              onChange={(e) => setPickedTestSetId(e.target.value)}
              className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
            >
              <option value="">— Selecionar —</option>
              {testSets?.map((ts) => (
                <option key={ts.id} value={ts.id} disabled={!ts.currentVersionId}>
                  {ts.name} {!ts.currentVersionId && '(sem versão Published)'}
                </option>
              ))}
            </select>
          </div>
          <div className="text-xs text-text-muted">
            EvaluatorConfig: <code>{evaluatorConfig?.config.name ?? '(none)'}</code>
            {evaluatorConfig?.currentVersion && (
              <> · rev {evaluatorConfig.currentVersion.revision}</>
            )}
          </div>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setEnqueueOpen(false)}>Cancelar</Button>
            <Button
              variant="primary"
              onClick={handleEnqueue}
              loading={enqueueMutation.isPending}
              disabled={!pickedTestSet?.currentVersionId}
            >
              Enfileirar
            </Button>
          </div>
          {enqueueMutation.error && (
            <div className="text-sm text-red-400">{(enqueueMutation.error as Error).message}</div>
          )}
        </div>
      </Modal>
    </div>
  )
}
