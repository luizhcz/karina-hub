import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { getAgent, type Agent } from '../api/agents'
import { listAgentVersions } from '../api/agentVersions'
import {
  createWorkflow,
  deploymentWorkflowId,
  formatRevisionLabel,
  getWorkflow,
  getWorkflowEnabledStatus,
  updateWorkflow,
  type Workflow,
  type WorkflowEnabledStatus,
} from '../api/workflows'
import { ApiError, friendlyError } from '../api/client'
import { getSystemInfo } from '../api/system'
import { HowToConsumeWorkflow } from '../components/HowToConsumeWorkflow'
import { WorkflowVersionsModal } from '../components/WorkflowVersionsModal'
import {
  ArrowLeftIcon,
  Badge,
  BoltIcon,
  Button,
  Card,
  CardHeader,
  CheckIcon,
  ErrorMessage,
  Spinner,
  cn,
} from '../ui'

// Heurística pra escolher InputMode do workflow auto-criado no deploy.
// Conversational sempre roda em Chat (workflow exige). Router declara
// uso em chat via metadata['x-router-for-chat']='true'. Demais tipos
// usam Standalone (default histórico — pipeline batch).
function inferInputMode(agent: Agent): 'Standalone' | 'Chat' {
  if (agent.type === 'Conversational') return 'Chat'
  if (
    agent.type === 'Router'
    && agent.metadata?.['x-router-for-chat']?.toLowerCase() === 'true'
  ) {
    return 'Chat'
  }
  return 'Standalone'
}

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

  const handleDeploy = async () => {
    if (!agent || !id) return
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
          inputMode: inferInputMode(agent),
          enableHumanInTheLoop: false,
          exposeAsAgent: false,
        },
        metadata: { deployedFromAgentId: id },
        visibility: 'project',
      })
      setWorkflow(wf)
      void refreshEnabledStatus(wf.id)
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
          inputMode: inferInputMode(agent),
          enableHumanInTheLoop: false,
          exposeAsAgent: false,
        },
        metadata: { deployedFromAgentId: id, pinnedAgentRevision: String(current.revision) },
        visibility: 'project',
      })
      setWorkflow(wf)
      void refreshEnabledStatus(wf.id)
      setRedeployFlash(`Workflow atualizado para a versão atual do agente (r${current.revision}).`)
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
          onOpenSandbox={() => navigate(`/implantacoes/${workflow.id}/sandbox`)}
          redeploying={redeploying}
          redeployError={redeployError}
          redeployFlash={redeployFlash}
        />
      ) : (
        <PendingView
          agent={agent}
          deploying={deploying}
          deployError={deployError}
          onDeploy={handleDeploy}
        />
      )}

      <WorkflowVersionsModal
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
}

function PendingView({
  agent,
  deploying,
  deployError,
  onDeploy,
}: PendingViewProps) {
  return (
    <div className="space-y-5">
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
  onOpenSandbox: () => void
  redeploying: boolean
  redeployError: string | null
  redeployFlash: string | null
}

function DeployedView({
  workflow,
  agent,
  publicBaseUrl,
  enabledStatus,
  onRedeploy,
  onOpenVersions,
  onOpenSandbox,
  redeploying,
  redeployError,
  redeployFlash,
}: DeployedViewProps) {
  return (
    <div className="space-y-5">
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
            <Field label="Versão do agente" value={formatRevisionLabel(pinnedAgentRevision(workflow))} mono />
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
            <Button size="sm" onClick={onOpenSandbox} leftIcon={<BoltIcon className="h-3.5 w-3.5" />}>
              Testar
            </Button>
          </div>
        </div>
      </Card>

      <HowToConsumeWorkflow workflowId={workflow.id} publicBaseUrl={publicBaseUrl} />
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
