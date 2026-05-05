import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { listAgentVersions } from '../api/agentVersions'
import {
  createWorkflow,
  deploymentWorkflowId,
  getWorkflow,
  getWorkflowEnabledStatus,
  listWorkflowVersions,
  rollbackWorkflow,
  updateWorkflow,
  type Workflow,
  type WorkflowEnabledStatus,
  type WorkflowVersion,
} from '../api/workflows'
import {
  type AutoDeployPreset,
  type AutoDeployResponse,
  listEvalRunsByAgent,
  runAutoDeploy,
} from '../api/profileEvaluation'
import { ApiError, friendlyError } from '../api/client'
import { getSystemInfo } from '../api/system'
import { getIdentity } from '../stores/identity'
import {
  ArrowLeftIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  CardHeader,
  CheckIcon,
  ErrorMessage,
  Modal,
  Spinner,
  cn,
} from '../ui'
import { PresetSelector } from './AgentDeploy/PresetSelector'
import { EvalStatusCard } from './AgentDeploy/EvalStatusCard'
import { RerunEvalCard } from './AgentDeploy/RerunEvalCard'

export function AgentDeploy() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [agent, setAgent] = useState<Agent | null>(null)
  const [workflow, setWorkflow] = useState<Workflow | null>(null)
  const [enabledStatus, setEnabledStatus] = useState<WorkflowEnabledStatus | null>(null)
  const [publicBaseUrl, setPublicBaseUrl] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [deploying, setDeploying] = useState(false)
  const [deployError, setDeployError] = useState<string | null>(null)

  // Eval auto-deploy state.
  const [preset, setPreset] = useState<AutoDeployPreset>('basic')
  const [confirmAdvanced, setConfirmAdvanced] = useState(false)
  const [autoDeploy, setAutoDeploy] = useState<AutoDeployResponse | null>(null)
  const [autoDeployRunning, setAutoDeployRunning] = useState(false)

  const refreshEnabledStatus = async (workflowId: string) => {
    try {
      const status = await getWorkflowEnabledStatus(workflowId)
      setEnabledStatus(status)
    } catch {
      setEnabledStatus(null)
    }
  }

  // No mount: carrega agente + tenta detectar deployment existente + busca a
  // baseUrl pública do backend (pra renderizar no exemplo de consumo).
  useEffect(() => {
    if (!id) return
    let cancelled = false

    const load = async () => {
      setLoading(true)
      setLoadError(null)
      try {
        const [a, info] = await Promise.all([getAgent(id), getSystemInfo()])
        if (cancelled) return
        setAgent(a)
        setPublicBaseUrl(info.publicBaseUrl)

        try {
          const wf = await getWorkflow(deploymentWorkflowId(id))
          if (!cancelled) {
            setWorkflow(wf)
            void refreshEnabledStatus(wf.id)
          }
        } catch (err) {
          if (err instanceof ApiError && err.status === 404) {
            // Esperado quando ainda não foi implantado.
          } else if (!cancelled) {
            setLoadError(friendlyError(err, 'Falha ao consultar implantação existente.'))
          }
        }

        // Hidrata último run de auto-deploy (após F5 mostra status atual).
        try {
          const runs = await listEvalRunsByAgent(id, 1)
          if (!cancelled && runs.length > 0) {
            const last = runs[0]
            const lastPreset = (last.triggerContext?.preset ?? 'basic') as AutoDeployPreset
            setPreset(lastPreset)
            setAutoDeploy({
              runId: last.runId,
              testSetVersionId: last.testSetVersionId,
              evaluatorConfigVersionId: last.evaluatorConfigVersionId,
              preset: lastPreset,
              caseCount: last.casesTotal,
              estimatedCostUsd: 0,
              estimatedDurationSeconds: 0,
              status: last.status,
              deduplicatedFromExisting: false,
              generatorFailed: false,
            })
          }
        } catch {
          // ignore — sem runs ainda é o caso comum
        }
      } catch (err) {
        if (!cancelled) setLoadError(friendlyError(err, 'Não foi possível carregar o agente.'))
      } finally {
        if (!cancelled) setLoading(false)
      }
    }

    load()
    return () => {
      cancelled = true
    }
  }, [id])

  // Trigger separado do auto-deploy de avaliação. Falha aqui não derruba o
  // workflow já deployado — soft gate. Frontend mostra "Avaliação não disponível"
  // se o gerador falhar. Aceita preset opcional pra rerun com preset diferente
  // do estado inicial (chamado pelo RerunEvalCard).
  const triggerEvalAutoDeploy = async (workflowId: string, presetOverride?: AutoDeployPreset) => {
    if (!id) return
    const usePreset = presetOverride ?? preset
    setAutoDeployRunning(true)
    try {
      const result = await runAutoDeploy(id, usePreset, workflowId)
      setAutoDeploy(result)
    } catch (err) {
      // Falha do auto-deploy é informativa, não bloqueia. EvalStatusCard mostra estado.
      setAutoDeploy({
        runId: null,
        testSetVersionId: null,
        evaluatorConfigVersionId: null,
        preset: usePreset,
        caseCount: 0,
        estimatedCostUsd: 0,
        estimatedDurationSeconds: 0,
        status: null,
        deduplicatedFromExisting: false,
        generatorFailed: true,
      })
      // friendlyError pode logar pra debugging futuro
      console.warn('[AgentDeploy] Falha no auto-deploy de avaliação:', friendlyError(err))
    } finally {
      setAutoDeployRunning(false)
    }
  }

  const handleDeploy = async () => {
    if (!agent || !id) return
    if (preset === 'advanced' && !confirmAdvanced) {
      setConfirmAdvanced(true)
      return
    }
    setConfirmAdvanced(false)
    setDeploying(true)
    setDeployError(null)
    try {
      const wf = await createWorkflow({
        id: deploymentWorkflowId(id),
        name: agent.name,
        description: agent.description ?? null,
        version: '1.0.0',
        orchestrationMode: 'Graph',
        agents: [{ agentId: id, agentVersionId: null }],
        executors: [],
        edges: [],
        configuration: {
          maxRounds: 1,
          timeoutSeconds: 300,
          checkpointMode: 'InMemory',
          inputMode: 'Standalone',
          enableHumanInTheLoop: false,
          exposeAsAgent: false,
        },
        metadata: { deployedFromAgentId: id },
        visibility: 'project',
      })
      setWorkflow(wf)
      void refreshEnabledStatus(wf.id)
      // Dispara avaliação async — não bloqueia retorno do deploy.
      void triggerEvalAutoDeploy(wf.id)
    } catch (err) {
      setDeployError(friendlyError(err, 'Não foi possível concluir a implantação.'))
    } finally {
      setDeploying(false)
    }
  }

  // "Atualizar agente": re-pina o workflow na AgentVersion mais recente do agente
  // (governança de upgrade — agente publicado em nova versão NÃO se reflete
  // automaticamente em workflows pinados; precisa do PM disparar essa ação).
  // Cria nova revisão do workflow porque o agentVersionId muda → ContentHash muda.
  const [redeploying, setRedeploying] = useState(false)
  const [redeployError, setRedeployError] = useState<string | null>(null)
  const [redeployFlash, setRedeployFlash] = useState<string | null>(null)

  const handleRedeploy = async () => {
    if (!agent || !id || !workflow) return
    setRedeploying(true)
    setRedeployError(null)
    setRedeployFlash(null)
    try {
      const versions = await listAgentVersions(id)
      const current = versions[0]
      if (!current) {
        setRedeployError('Esse agente ainda não tem nenhuma versão registrada — implante depois de aprovar pelo menos uma revisão.')
        return
      }
      const wf = await updateWorkflow(workflow.id, {
        id: workflow.id,
        name: workflow.name,
        description: workflow.description ?? null,
        version: '1.0.0',
        orchestrationMode: 'Graph',
        agents: [{ agentId: id, agentVersionId: current.agentVersionId }],
        executors: [],
        edges: [],
        configuration: {
          maxRounds: 1,
          timeoutSeconds: 300,
          checkpointMode: 'InMemory',
          inputMode: 'Standalone',
          enableHumanInTheLoop: false,
          exposeAsAgent: false,
        },
        metadata: { deployedFromAgentId: id, pinnedAgentRevision: String(current.revision) },
        visibility: 'project',
      })
      setWorkflow(wf)
      void refreshEnabledStatus(wf.id)
      setRedeployFlash(`Workflow atualizado para a versão atual do agente (r${current.revision}).`)
      // Nova AgentVersion → fura o dedup `(agentVersionId, preset)` no backend
      // automaticamente, então a avaliação roda fresh sem precisar de cooldown.
      void triggerEvalAutoDeploy(wf.id)
    } catch (err) {
      setRedeployError(friendlyError(err, 'Não foi possível atualizar o agente do workflow.'))
    } finally {
      setRedeploying(false)
    }
  }

  const [versionsOpen, setVersionsOpen] = useState(false)

  if (loading) {
    return (
      <Card className="mx-auto max-w-5xl flex items-center justify-center py-12">
        <Spinner className="h-6 w-6 text-fg-muted" />
      </Card>
    )
  }
  if (loadError) return <ErrorMessage message={loadError} className="mx-auto max-w-5xl" />
  if (!agent) return null

  return (
    <div className="mx-auto max-w-5xl space-y-6">
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate('/agentes?tab=published')}
          leftIcon={<ArrowLeftIcon className="h-4 w-4" />}
        >
          Voltar
        </Button>
        <div className="min-w-0 flex-1">
          <h1 className="truncate text-2xl font-semibold tracking-tight">
            Implantação — {agent.name}
          </h1>
          <p className="mt-1 text-xs text-fg-muted">
            Provisiona um workflow Graph single-agent que expõe esse agente para consumo via API.
          </p>
        </div>
      </div>

      {workflow ? (
        <DeployedView
          workflow={workflow}
          agent={agent}
          publicBaseUrl={publicBaseUrl}
          enabledStatus={enabledStatus}
          onRedeploy={handleRedeploy}
          onOpenVersions={() => setVersionsOpen(true)}
          redeploying={redeploying}
          redeployError={redeployError}
          redeployFlash={redeployFlash}
          autoDeploy={autoDeploy}
          autoDeployRunning={autoDeployRunning}
          onRetryAutoDeploy={() => triggerEvalAutoDeploy(workflow.id)}
          onRerunEval={(p) => triggerEvalAutoDeploy(workflow.id, p)}
        />
      ) : (
        <PendingView
          agent={agent}
          deploying={deploying}
          deployError={deployError}
          onDeploy={handleDeploy}
          preset={preset}
          onPresetChange={setPreset}
        />
      )}

      <Modal
        open={confirmAdvanced}
        onClose={() => setConfirmAdvanced(false)}
        title="Confirmar avaliação avançada"
        description="Preset Avançada gera 15 cases × 8 métricas MEAI."
        size="sm"
        footer={
          <div className="flex justify-end gap-2">
            <Button variant="ghost" onClick={() => setConfirmAdvanced(false)}>
              Cancelar
            </Button>
            <Button onClick={handleDeploy}>
              Confirmar e implantar
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          Esta avaliação pode custar até <span className="font-semibold text-fg">~$0.50 USD</span> por implantação. Confirma?
        </p>
      </Modal>

      <VersionsModal
        open={versionsOpen}
        workflow={workflow}
        onClose={() => setVersionsOpen(false)}
        onRolledBack={(wf) => {
          setWorkflow(wf)
          void refreshEnabledStatus(wf.id)
        }}
      />
    </div>
  )
}

interface PendingViewProps {
  agent: Agent
  deploying: boolean
  deployError: string | null
  onDeploy: () => void
  preset: AutoDeployPreset
  onPresetChange: (preset: AutoDeployPreset) => void
}

function PendingView({
  agent,
  deploying,
  deployError,
  onDeploy,
  preset,
  onPresetChange,
}: PendingViewProps) {
  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Avaliação automática"
          description="Toda implantação dispara uma avaliação sintética em background pra dar trilha auditável de qualidade."
        />
        <PresetSelector value={preset} onChange={onPresetChange} disabled={deploying} />
      </Card>

      <Card className="space-y-5">
        <CardHeader
          title="Configuração do workflow"
          description="Esses parâmetros são fixos para a implantação MVP. Você pode evoluir o workflow depois pela API."
        />
        <dl className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field label="Nome" value={agent.name} />
          <Field label="Modo" value="Graph" />
          <Field label="Versão" value="1.0.0" />
          <Field label="Agente referenciado" value={agent.id} mono />
          <Field label="Max rounds" value="1" />
          <Field label="Timeout" value="5 min" />
          <Field label="Checkpoint" value="InMemory" />
          <Field label="Input mode" value="Standalone" />
          <Field label="Trigger" value="OnDemand" />
        </dl>
        {deployError && <ErrorMessage message={deployError} />}
        <div className="flex items-center justify-end">
          <Button onClick={onDeploy} loading={deploying} leftIcon={<BoltIcon className="h-4 w-4" />}>
            {deploying ? 'Provisionando workflow…' : 'Implantar em produção'}
          </Button>
        </div>
      </Card>
    </div>
  )
}

interface DeployedViewProps {
  workflow: Workflow
  agent: Agent
  publicBaseUrl: string | null
  enabledStatus: WorkflowEnabledStatus | null
  onRedeploy: () => void
  onOpenVersions: () => void
  redeploying: boolean
  redeployError: string | null
  redeployFlash: string | null
  autoDeploy: AutoDeployResponse | null
  autoDeployRunning: boolean
  onRetryAutoDeploy: () => void
  onRerunEval: (preset: AutoDeployPreset) => void
}

function DeployedView({
  workflow,
  agent,
  publicBaseUrl,
  enabledStatus,
  onRedeploy,
  onOpenVersions,
  redeploying,
  redeployError,
  redeployFlash,
  autoDeploy,
  autoDeployRunning,
  onRetryAutoDeploy,
  onRerunEval,
}: DeployedViewProps) {
  const identity = useMemo(() => getIdentity(), [])
  const projectId = identity?.projectId ?? '<seu-project-id>'
  const account = identity?.account ?? '<seu-account>'
  const baseUrl = publicBaseUrl ?? '<base-url-do-backend>'
  const triggerUrl = `${baseUrl}/api/aihub/workflows/${workflow.id}/trigger`

  const headers: Array<{ key: string; value: string }> = [
    { key: 'Content-Type', value: 'application/json' },
    { key: 'x-efs-account', value: account },
    { key: 'x-efs-project-id', value: projectId },
  ]

  const bodyExample = JSON.stringify({ input: 'Olá, faça uma análise sobre…', metadata: {} }, null, 2)

  const triggerResponseExample = JSON.stringify(
    {
      executionId: '04bf1f50-763c-47ed-94e3-34ab3f47ea85',
      statusUrl: `${baseUrl}/api/aihub/executions/04bf1f50-763c-47ed-94e3-34ab3f47ea85`,
    },
    null,
    2,
  )

  const executionUrl = `${baseUrl}/api/aihub/executions/{executionId}`

  const executionResponseExample = JSON.stringify(
    {
      executionId: '04bf1f50-763c-47ed-94e3-34ab3f47ea85',
      workflowId: workflow.id,
      status: 'Completed',
      input: 'Olá, faça uma análise sobre…',
      output: '{"resumo":"…","numeros":[…],"avisos":[…]}',
      errorMessage: null,
      startedAt: '2026-05-03T21:48:59.97Z',
      completedAt: '2026-05-03T21:49:03.56Z',
      metadata: {},
    },
    null,
    2,
  )


  return (
    <div className="space-y-5">
      {/* Card de status da avaliação — full width acima do grid de 2 colunas. */}
      {(autoDeploy || autoDeployRunning) && (
        <EvalStatusCard
          runId={autoDeploy?.runId ?? null}
          preset={autoDeploy?.preset ?? 'basic'}
          generatorFailed={autoDeploy?.generatorFailed ?? false}
          estimatedCostUsd={autoDeploy?.estimatedCostUsd ?? 0}
          caseCount={autoDeploy?.caseCount ?? 0}
          onRetry={onRetryAutoDeploy}
        />
      )}

      <RerunEvalCard
        defaultPreset={autoDeploy?.preset ?? 'basic'}
        running={autoDeployRunning}
        onRerun={onRerunEval}
        deduplicatedHint={autoDeploy?.deduplicatedFromExisting ?? false}
      />


      <div className="grid grid-cols-1 gap-5 lg:grid-cols-2">
      <Card className="space-y-3">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-success/15 text-success">
            <CheckIcon className="h-5 w-5" />
          </div>
          <div>
            <h2 className="text-base font-semibold text-fg">Implantado</h2>
            <p className="text-xs text-fg-muted">Workflow ativo e pronto para receber requisições.</p>
          </div>
        </div>
        <dl className="space-y-2 pt-2">
          <Field label="Workflow ID" value={workflow.id} mono />
          <Field label="Nome" value={workflow.name} />
          <Field label="Modo" value={workflow.orchestrationMode} />
          <Field label="Agente" value={agent.id} mono />
          {pinnedAgentRevision(workflow) && (
            <Field label="Versão do agente" value={`r${pinnedAgentRevision(workflow)}`} mono />
          )}
          <Field label="Atualizado em" value={formatAbsolute(workflow.updatedAt)} />
        </dl>
        <div className="flex flex-wrap items-center gap-2 pt-2">
          {enabledStatus ? (
            enabledStatus.enabled ? (
              <Badge tone="success">
                Habilitado · {enabledStatus.enabledAgents}/{enabledStatus.totalAgents} agente
                {enabledStatus.totalAgents === 1 ? '' : 's'} ativo
                {enabledStatus.enabledAgents === 1 ? '' : 's'}
              </Badge>
            ) : (
              <Badge tone="warning">
                Desabilitado · todos os agentes referenciados estão desligados
              </Badge>
            )
          ) : (
            <Badge tone="success">Pronto para consumo</Badge>
          )}
          {workflow.currentRevision != null && (
            <Badge tone="accent">
              Workflow ativo: r{workflow.currentRevision}
            </Badge>
          )}
        </div>
        {redeployFlash && (
          <div className="rounded-md border border-success/30 bg-success/10 px-3 py-2 text-xs text-success">
            {redeployFlash}
          </div>
        )}
        {redeployError && <ErrorMessage message={redeployError} />}
        <div className="flex flex-wrap items-end justify-between gap-2 pt-2">
          <p className="max-w-xs text-[11px] leading-snug text-fg-muted">
            <span className="font-semibold text-fg">Atualizar agente</span> sobe o workflow para a versão atual do agente (cria uma nova revisão).
          </p>
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="sm" onClick={onOpenVersions}>
              Versões
            </Button>
            <Button variant="secondary" size="sm" onClick={onRedeploy} loading={redeploying}>
              Atualizar agente
            </Button>
          </div>
        </div>
      </Card>

      <Card className="space-y-4">
        <CardHeader
          title="Como consumir"
          description="O workflow é assíncrono: dispara com POST, retorna 202 + executionId, e o resultado é lido fazendo polling no GET de execução."
        />
        <div className="space-y-5">
          <div className="space-y-3">
            <StepHeading number={1} title="Disparar a execução" />
            <Section label="Endpoint">
              <CodeBlock value={`POST ${triggerUrl}`} />
            </Section>
            <Section label="Headers">
              <table className="w-full text-xs">
                <tbody>
                  {headers.map((h) => (
                    <tr key={h.key} className="border-b border-border last:border-b-0">
                      <td className="py-1.5 pr-3 font-mono text-fg-muted">{h.key}</td>
                      <td className="py-1.5 font-mono text-fg">{h.value}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Section>
            <Section label="Body (request)">
              <CodeBlock value={bodyExample} />
            </Section>
            <Section label="Resposta (202)">
              <CodeBlock value={triggerResponseExample} />
            </Section>
          </div>

          <div className="space-y-3 border-t border-border pt-5">
            <StepHeading number={2} title="Ler o resultado" />
            <p className="text-xs text-fg-muted">
              Faça polling neste endpoint até <code className="font-mono">status</code> virar <code className="font-mono">Completed</code> (ou <code className="font-mono">Failed</code>/<code className="font-mono">Cancelled</code>). O output do agente vem como string em <code className="font-mono">output</code> — geralmente JSON quando o agente tem schema estruturado.
            </p>
            <Section label="Endpoint">
              <CodeBlock value={`GET ${executionUrl}`} />
            </Section>
            <Section label="Headers">
              <table className="w-full text-xs">
                <tbody>
                  {headers
                    .filter((h) => h.key !== 'Content-Type')
                    .map((h) => (
                      <tr key={h.key} className="border-b border-border last:border-b-0">
                        <td className="py-1.5 pr-3 font-mono text-fg-muted">{h.key}</td>
                        <td className="py-1.5 font-mono text-fg">{h.value}</td>
                      </tr>
                    ))}
                </tbody>
              </table>
            </Section>
            <Section label="Resposta (200) — exemplo quando concluída">
              <CodeBlock value={executionResponseExample} />
            </Section>
          </div>

        </div>
      </Card>
      </div>
    </div>
  )
}

interface FieldProps {
  label: string
  value: string
  mono?: boolean
}

function Field({ label, value, mono }: FieldProps) {
  return (
    <div>
      <dt className="text-[11px] uppercase tracking-wider text-fg-dim">{label}</dt>
      <dd className={cn('mt-0.5 text-sm text-fg', mono && 'font-mono text-xs')}>{value}</dd>
    </div>
  )
}

function pinnedAgentRevision(workflow: Workflow): string | null {
  const md = (workflow as { metadata?: Record<string, string> | null }).metadata
  return md?.pinnedAgentRevision ?? null
}

interface VersionsModalProps {
  open: boolean
  workflow: Workflow | null
  onClose: () => void
  onRolledBack: (workflow: Workflow) => void
}

function VersionsModal({ open, workflow, onClose, onRolledBack }: VersionsModalProps) {
  const [versions, setVersions] = useState<WorkflowVersion[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [rollingBackId, setRollingBackId] = useState<string | null>(null)
  const [confirm, setConfirm] = useState<WorkflowVersion | null>(null)

  useEffect(() => {
    if (!open || !workflow) return
    let cancelled = false
    setLoading(true)
    setError(null)
    setVersions([])
    listWorkflowVersions(workflow.id)
      .then((list) => {
        if (!cancelled) setVersions(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(friendlyError(err, 'Não foi possível carregar as versões.'))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [open, workflow])

  const performRollback = async (target: WorkflowVersion) => {
    if (!workflow) return
    setRollingBackId(target.workflowVersionId)
    setError(null)
    try {
      const wf = await rollbackWorkflow(workflow.id, target.workflowVersionId)
      onRolledBack(wf)
      // Recarrega timeline (rollback gerou nova revision com mesmo conteúdo).
      const refreshed = await listWorkflowVersions(workflow.id)
      setVersions(refreshed)
      setConfirm(null)
    } catch (err) {
      setError(friendlyError(err, 'Não foi possível restaurar essa versão.'))
    } finally {
      setRollingBackId(null)
    }
  }

  return (
    <>
      <Modal
        open={open}
        onClose={onClose}
        size="lg"
        title="Versões do workflow"
        description={workflow?.name}
      >
        <p className="mb-4 text-xs text-fg-muted">
          Timeline da definição do <strong>workflow</strong> (independente da timeline do agente).
          Cada edição cria uma nova revisão; <strong>Restaurar</strong> volta o estado em runtime para o snapshot
          escolhido — o histórico permanece intacto.
        </p>
        {loading && (
          <div className="flex items-center justify-center py-8">
            <Spinner className="h-6 w-6 text-fg-muted" />
          </div>
        )}
        {!loading && error && <ErrorMessage message={error} />}
        {!loading && !error && versions.length === 0 && (
          <p className="py-6 text-center text-sm text-fg-muted">Nenhuma versão registrada.</p>
        )}
        {!loading && !error && versions.length > 0 && (
          <ol className="relative space-y-3 border-l border-border pl-5">
            {versions.map((v) => {
              const isCurrent = workflow?.currentVersionId === v.workflowVersionId
              return (
                <li key={v.workflowVersionId} className="relative">
                  <span
                    className={cn(
                      'absolute -left-[27px] top-1.5 h-3 w-3 rounded-full ring-2 ring-surface',
                      isCurrent ? 'bg-success' : 'bg-fg-muted',
                    )}
                    aria-hidden="true"
                  />
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="font-mono text-sm font-semibold text-fg">r{v.revision}</span>
                    {isCurrent && <Badge tone="success">atual</Badge>}
                    <span className="font-mono text-[10px] uppercase tracking-wider text-fg-dim">
                      {v.contentHash.slice(0, 12)}
                    </span>
                    <span className="text-[11px] text-fg-dim">{formatAbsolute(v.createdAt)}</span>
                  </div>
                  {(v.createdBy || v.changeReason) && (
                    <p className="mt-1 text-xs text-fg-muted">
                      {v.createdBy && (
                        <>
                          por <span className="font-mono text-fg">{v.createdBy}</span>
                        </>
                      )}
                      {v.changeReason && (
                        <>
                          {v.createdBy && ' · '}
                          {v.changeReason}
                        </>
                      )}
                    </p>
                  )}
                  {!isCurrent && (
                    <div className="mt-2">
                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => setConfirm(v)}
                        loading={rollingBackId === v.workflowVersionId}
                      >
                        Restaurar
                      </Button>
                    </div>
                  )}
                </li>
              )
            })}
          </ol>
        )}
      </Modal>

      <Modal
        open={confirm !== null}
        onClose={() => rollingBackId === null && setConfirm(null)}
        title="Restaurar versão"
        description={confirm ? `Voltar pra r${confirm.revision}` : undefined}
        footer={
          <div className="flex items-center justify-end gap-2">
            <Button
              variant="ghost"
              onClick={() => setConfirm(null)}
              disabled={rollingBackId !== null}
            >
              Cancelar
            </Button>
            <Button
              onClick={() => confirm && performRollback(confirm)}
              loading={rollingBackId !== null}
            >
              Restaurar r{confirm?.revision}
            </Button>
          </div>
        }
      >
        <p className="text-sm text-fg-muted">
          O rollback é <strong>append-only</strong> — não apaga as revisões intermediárias. Cria uma
          nova revisão com o conteúdo idêntico à versão alvo, que passa a ser a atual em runtime.
        </p>
      </Modal>
    </>
  )
}

function StepHeading({ number, title }: { number: number; title: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="flex h-6 w-6 shrink-0 items-center justify-center rounded-full bg-accent text-[11px] font-semibold text-accent-contrast">
        {number}
      </span>
      <h3 className="text-sm font-semibold text-fg">{title}</h3>
    </div>
  )
}

function Section({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1 text-[11px] font-semibold uppercase tracking-wider text-fg-muted">{label}</p>
      {children}
    </div>
  )
}

function CodeBlock({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)
  const onCopy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    } catch {
      /* noop */
    }
  }
  return (
    <div className="relative">
      <pre className="overflow-x-auto rounded-md border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-relaxed text-fg">
        {value}
      </pre>
      <button
        type="button"
        onClick={onCopy}
        className="absolute right-2 top-1.5 rounded-md border border-border bg-surface px-2 py-0.5 text-[10px] font-semibold text-fg-muted transition hover:text-fg"
      >
        {copied ? 'Copiado' : 'Copiar'}
      </button>
    </div>
  )
}

function formatAbsolute(iso: string): string {
  const date = new Date(iso)
  if (Number.isNaN(date.getTime())) return iso
  return date.toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}
