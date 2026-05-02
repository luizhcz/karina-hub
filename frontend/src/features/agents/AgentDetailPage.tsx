import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Card } from '../../shared/ui/Card'
import { ConfirmDialog } from '../../shared/ui/ConfirmDialog'
import { Tabs } from '../../shared/ui/Tabs'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgent, useUpdateAgent, useUpdateAgentVisibility, useUpdateAgentEnabled } from '../../api/agents'
import type { AgentVisibility } from '../../api/agents'
import { ApiError } from '../../api/client'
import { usePromptVersions } from '../../api/prompts'
import { useActivePrompt } from '../../api/prompts'
import { AgentForm } from './components/AgentForm'
import { PromptsPanel } from './components/PromptsPanel'
import { VersionsPanel } from './components/VersionsPanel'
import { SandboxPanel } from './components/SandboxPanel'
import { AgentEvaluationsTab } from './evaluations/AgentEvaluationsTab'
import { formToRequest } from './formToRequest'
import { toast } from '../../stores/toast'
import { useProjectStore } from '../../stores/project'
import type { AgentFormValues } from './types'

type TabKey = 'config' | 'prompts' | 'versions' | 'sandbox' | 'evaluations'

interface AgentDetailPageProps {
  initialTab?: TabKey
}

export function AgentDetailPage({ initialTab = 'config' }: AgentDetailPageProps) {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: agent, isLoading, error, refetch } = useAgent(id!, !!id)
  const updateMutation = useUpdateAgent()
  const visibilityMutation = useUpdateAgentVisibility()
  const enabledMutation = useUpdateAgentEnabled()
  const { data: promptVersions } = usePromptVersions(id!, !!id)
  const { data: activePrompt } = useActivePrompt(id!, !!id)

  const [activeTab, setActiveTab] = useState<string>(initialTab)
  const [pendingVisibility, setPendingVisibility] = useState<AgentVisibility | null>(null)
  const [pendingDisable, setPendingDisable] = useState(false)

  if (isLoading) return <PageLoader />
  if (error || !agent) return <ErrorCard message="Erro ao carregar agente." onRetry={refetch} />

  const currentVisibility: AgentVisibility = agent.visibility ?? 'project'
  const isGlobal = currentVisibility === 'global'
  const isEnabled = agent.enabled !== false
  const currentProjectId = useProjectStore.getState().projectId
  const isOwnedByCurrentProject = !agent.originProjectId || agent.originProjectId === currentProjectId

  const applyEnabledChange = (nextEnabled: boolean) => {
    enabledMutation.mutate(
      { id: id!, enabled: nextEnabled },
      {
        onSuccess: () => {
          toast.success(nextEnabled
            ? 'Agente habilitado novamente. Workflows callers voltam a invocá-lo.'
            : 'Agente desabilitado. Workflows que o referenciam vão pulá-lo em runtime.')
          setPendingDisable(false)
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao alterar Enabled.'
          toast.error(msg)
          setPendingDisable(false)
        },
      },
    )
  }

  const handleEnabledClick = () => {
    if (isEnabled) {
      // Desabilitar tem impacto runtime imediato em workflows callers — pede confirmação.
      setPendingDisable(true)
    } else {
      // Habilitar é reversível e sem efeito runtime negativo — direto.
      applyEnabledChange(true)
    }
  }

  const confirmVisibilityChange = () => {
    if (!pendingVisibility) return
    visibilityMutation.mutate(
      { id: id!, visibility: pendingVisibility },
      {
        onSuccess: () => {
          toast.success(
            pendingVisibility === 'global'
              ? 'Agent agora compartilhável com outros projetos do tenant.'
              : 'Agent agora restrito ao projeto dono.',
          )
          setPendingVisibility(null)
        },
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao alterar visibility.'
          toast.error(msg)
          setPendingVisibility(null)
        },
      },
    )
  }

  const handleSubmit = (values: AgentFormValues) => {
    const result = formToRequest(values)
    if (!result.ok) {
      toast.error(result.error)
      return
    }
    updateMutation.mutate(
      { id: id!, body: result.body },
      {
        onSuccess: () => navigate('/agents'),
        onError: (err) => {
          const msg = err instanceof ApiError ? err.message : 'Erro ao salvar agente.'
          toast.error(msg)
        },
      },
    )
  }

  const tabItems = [
    { key: 'config', label: 'Configuracao' },
    { key: 'prompts', label: 'Prompts', badge: promptVersions?.length },
    { key: 'versions', label: 'Versoes' },
    { key: 'sandbox', label: 'Sandbox' },
    { key: 'evaluations', label: 'Evaluations' },
  ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div className="flex items-center gap-3 flex-1 min-w-0">
          <h1 className="text-2xl font-bold text-text-primary truncate">{agent.name}</h1>
          {activePrompt && (
            <Badge variant="purple">{activePrompt.versionId}</Badge>
          )}
        </div>
      </div>

      <Card title="Compartilhamento">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1">
            <div className="flex items-center gap-2 flex-wrap">
              {isGlobal ? (
                <Badge variant="purple">🌐 Global</Badge>
              ) : (
                <Badge variant="gray">🔒 Privado</Badge>
              )}
              {!isOwnedByCurrentProject && (
                <Badge variant="yellow">Somente leitura — projeto {agent.originProjectId}</Badge>
              )}
              {isGlobal && agent.allowedProjectIds && agent.allowedProjectIds.length > 0 && (
                <Badge variant="blue">
                  🔒 Whitelist · {agent.allowedProjectIds.length} projeto(s)
                </Badge>
              )}
              <p className="text-sm text-text-secondary">
                {isGlobal
                  ? agent.allowedProjectIds && agent.allowedProjectIds.length > 0
                    ? `Visível apenas pra projetos da whitelist (${agent.allowedProjectIds.join(', ')}).`
                    : 'Visível e usável em workflows de todos os projetos do tenant.'
                  : 'Visível apenas no projeto dono.'}
              </p>
            </div>
            <p className="text-xs text-text-dimmed mt-2 leading-relaxed">
              {!isOwnedByCurrentProject
                ? 'Você está vendo um agent compartilhado de outro projeto. Apenas o projeto dono pode alterar visibility ou conteúdo.'
                : isGlobal
                  ? 'Outros projetos do tenant podem referenciar este agent em workflows. Credenciais e skills continuam resolvidas no contexto deste projeto (owner).'
                  : 'Tornar global permite que workflows de outros projetos do mesmo tenant referenciem este agent. Tenant boundary é estrito (cross-tenant nunca é permitido).'}
            </p>
          </div>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setPendingVisibility(isGlobal ? 'project' : 'global')}
            disabled={visibilityMutation.isPending || !isOwnedByCurrentProject}
          >
            {isGlobal ? 'Tornar privado' : 'Compartilhar globalmente'}
          </Button>
        </div>
      </Card>

      <Card title="Habilitação">
        <div className="flex items-start justify-between gap-4">
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2 mb-2">
              {isEnabled
                ? <Badge variant="green">✓ Habilitado</Badge>
                : <Badge variant="red">✗ Desabilitado</Badge>}
            </div>
            <p className="text-sm text-text-secondary">
              {isEnabled
                ? 'Agente ativo. Workflows que o referenciam invocam normalmente em runtime.'
                : 'Agente desligado pelo dono. Workflows que o referenciam continuam saváveis, mas runtime pula o agente (Sequential continua pipeline; Graph ignora edges; Handoff é rejeitado no save).'}
            </p>
            <p className="text-xs text-text-dimmed mt-2 leading-relaxed">
              Use desabilitar pra manutenção temporária (atualizar tools, trocar model) sem precisar migrar workflows callers.
            </p>
          </div>
          <Button
            variant="secondary"
            size="sm"
            onClick={handleEnabledClick}
            disabled={enabledMutation.isPending || !isOwnedByCurrentProject}
          >
            {isEnabled ? 'Desabilitar' : 'Habilitar'}
          </Button>
        </div>
      </Card>

      <Tabs items={tabItems} active={activeTab} onChange={setActiveTab} />

      {/* Todos os panels são renderizados e escondidos via CSS — preserva estado ao trocar de tab */}
      <div style={{ display: activeTab === 'config' ? 'block' : 'none' }}>
        <AgentForm
          initialValues={agent}
          onSubmit={handleSubmit}
          loading={updateMutation.isPending}
        />
      </div>

      <div style={{ display: activeTab === 'prompts' ? 'block' : 'none' }}>
        <PromptsPanel agentId={id!} currentInstructions={agent.instructions} />
      </div>

      <div style={{ display: activeTab === 'versions' ? 'block' : 'none' }}>
        <VersionsPanel agentId={id!} />
      </div>

      <div style={{ display: activeTab === 'sandbox' ? 'block' : 'none' }}>
        <SandboxPanel agentId={id!} agentName={agent.name} />
      </div>

      <div style={{ display: activeTab === 'evaluations' ? 'block' : 'none' }}>
        <AgentEvaluationsTab agentId={id!} />
      </div>

      <ConfirmDialog
        open={pendingVisibility !== null}
        onClose={() => setPendingVisibility(null)}
        onConfirm={confirmVisibilityChange}
        title={pendingVisibility === 'global' ? 'Tornar agent global?' : 'Tornar agent privado?'}
        message={
          pendingVisibility === 'global'
            ? 'Outros projetos do tenant passarão a referenciar este agent em workflows. Credenciais permanecem resolvidas no projeto dono. Você pode reverter a qualquer momento.'
            : 'O agent deixará de aparecer em outros projetos. Workflows de outros projetos que já referenciavam ficarão com erro de resolução até serem ajustados.'
        }
        confirmLabel={pendingVisibility === 'global' ? 'Compartilhar' : 'Tornar privado'}
        loading={visibilityMutation.isPending}
      />

      <ConfirmDialog
        open={pendingDisable}
        onClose={() => setPendingDisable(false)}
        onConfirm={() => applyEnabledChange(false)}
        title="Desabilitar agent?"
        message="Workflows que referenciam este agent continuam saváveis, mas runtime vai pulá-lo na execução (Sequential continua pipeline com step ausente; Graph ignora edges órfãs; Handoff já existente fica em erro até reabilitar). Você pode reverter a qualquer momento."
        confirmLabel="Desabilitar"
        loading={enabledMutation.isPending}
      />
    </div>
  )
}
