import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Tabs } from '../../shared/ui/Tabs'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgent, useUpdateAgent } from '../../api/agents'
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
  const { data: promptVersions } = usePromptVersions(id!, !!id)
  const { data: activePrompt } = useActivePrompt(id!, !!id)

  const [activeTab, setActiveTab] = useState<string>(initialTab)

  if (isLoading) return <PageLoader />
  if (error || !agent) return <ErrorCard message="Erro ao carregar agente." onRetry={refetch} />

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
    </div>
  )
}
