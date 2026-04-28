import { useEffect, useState } from 'react'
import { useProjectStore } from '../../../stores/project'
import {
  useRegressionConfig,
  useUpdateRegressionConfig,
  useTestSets,
  useEvaluatorConfig,
} from '../../../api/evaluations'
import { Button } from '../../../shared/ui/Button'
import { Card } from '../../../shared/ui/Card'
import { Badge } from '../../../shared/ui/Badge'

interface Props {
  agentId: string
}

export function RegressionConfigPanel({ agentId }: Props) {
  const projectId = useProjectStore((s) => s.projectId) ?? 'default'
  const { data: config } = useRegressionConfig(agentId)
  const { data: testSets } = useTestSets(projectId)
  const { data: evaluatorConfig } = useEvaluatorConfig(agentId)
  const updateMutation = useUpdateRegressionConfig()

  const [testSetId, setTestSetId] = useState<string>('')
  const [evaluatorVersionId, setEvaluatorVersionId] = useState<string>('')

  useEffect(() => {
    if (config) {
      setTestSetId(config.regressionTestSetId ?? '')
      setEvaluatorVersionId(config.regressionEvaluatorConfigVersionId ?? '')
    }
  }, [config])

  const handleSave = async () => {
    await updateMutation.mutateAsync({
      agentId,
      body: {
        regressionTestSetId: testSetId || null,
        regressionEvaluatorConfigVersionId: evaluatorVersionId || null,
      },
    })
  }

  const handleClear = async () => {
    await updateMutation.mutateAsync({
      agentId,
      body: {
        regressionTestSetId: null,
        regressionEvaluatorConfigVersionId: null,
      },
    })
  }

  const evaluatorVersionOptions = evaluatorConfig?.currentVersion
    ? [
        {
          id: evaluatorConfig.currentVersion.evaluatorConfigVersionId,
          label: `rev ${evaluatorConfig.currentVersion.revision} (current)`,
        },
      ]
    : []

  const enabled = !!testSetId && !!evaluatorVersionId
  const missingNudge = !config?.autotriggerEnabled

  return (
    <Card>
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-lg font-semibold">Regression Config (Autotrigger)</h3>
        <Badge variant={config?.autotriggerEnabled ? 'green' : 'gray'}>
          {config?.autotriggerEnabled ? 'Habilitado' : 'Desabilitado'}
        </Badge>
      </div>

      {missingNudge && (
        <div className="mb-3 rounded-md border border-amber-500/30 bg-amber-500/10 px-3 py-2 text-xs text-amber-300">
          ⚠ Regression baseline não configurada. Publicações de novas <code>AgentVersion</code>
          NÃO disparam eval automática até definir TestSet + EvaluatorConfig version aqui.
        </div>
      )}

      <div className="space-y-3">
        <div>
          <label className="block text-xs text-text-muted mb-1">Test Set</label>
          <select
            value={testSetId}
            onChange={(e) => setTestSetId(e.target.value)}
            className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
          >
            <option value="">— Selecionar —</option>
            {testSets?.map((ts) => (
              <option key={ts.id} value={ts.id}>
                {ts.name} {ts.visibility === 'global' && '(global)'}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs text-text-muted mb-1">EvaluatorConfig Version</label>
          <select
            value={evaluatorVersionId}
            onChange={(e) => setEvaluatorVersionId(e.target.value)}
            className="w-full px-3 py-2 text-sm rounded-md bg-bg-secondary border border-border-secondary text-text-primary"
            disabled={evaluatorVersionOptions.length === 0}
          >
            <option value="">
              {evaluatorVersionOptions.length === 0
                ? '— Defina EvaluatorConfig primeiro na aba Config —'
                : '— Selecionar —'}
            </option>
            {evaluatorVersionOptions.map((v) => (
              <option key={v.id} value={v.id}>{v.label}</option>
            ))}
          </select>
        </div>

        <div className="flex justify-between items-center pt-2">
          <div className="text-xs text-text-muted">
            {enabled
              ? '✓ Autotrigger ativará em todo publish de AgentVersion deste agente.'
              : 'Selecione TestSet + EvaluatorConfig version para habilitar.'}
          </div>
          <div className="flex gap-2">
            {config?.autotriggerEnabled && (
              <Button variant="ghost" size="sm" onClick={handleClear} loading={updateMutation.isPending}>
                Limpar
              </Button>
            )}
            <Button variant="primary" onClick={handleSave} loading={updateMutation.isPending}>
              Salvar
            </Button>
          </div>
        </div>

        {updateMutation.error && (
          <div className="text-sm text-red-400">{(updateMutation.error as Error).message}</div>
        )}
      </div>
    </Card>
  )
}
