import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { Tabs } from '../../shared/ui/Tabs'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgent, useUpdateAgent } from '../../api/agents'
import { usePromptVersions } from '../../api/prompts'
import { useActivePrompt } from '../../api/prompts'
import { AgentForm } from './components/AgentForm'
import { PromptsPanel } from './components/PromptsPanel'
import { VersionsPanel } from './components/VersionsPanel'
import { SandboxPanel } from './components/SandboxPanel'
import type { AgentFormValues } from './types'
import type { CreateAgentRequest } from '../../api/agents'

type TabKey = 'config' | 'prompts' | 'versions' | 'sandbox'

interface AgentDetailPageProps {
  initialTab?: TabKey
}

function formToRequest(values: AgentFormValues): CreateAgentRequest {
  return {
    id: values.id,
    name: values.name,
    description: values.description || undefined,
    model: {
      deploymentName: values.model.deploymentName,
      temperature: values.model.temperature,
      maxTokens: values.model.maxTokens,
    },
    provider: values.provider.type
      ? {
          type: values.provider.type,
          clientType: values.provider.clientType || undefined,
          endpoint: values.provider.endpoint || undefined,
        }
      : undefined,
    instructions: values.instructions || undefined,
    tools: values.tools.length > 0
      ? values.tools.map((name) => ({ type: 'function', name }))
      : undefined,
    structuredOutput: values.structuredOutput.responseFormat !== 'text'
      ? {
          responseFormat: values.structuredOutput.responseFormat,
          schemaName: values.structuredOutput.schemaName || undefined,
          schemaDescription: values.structuredOutput.schemaDescription || undefined,
          schema: values.structuredOutput.schema
            ? JSON.parse(values.structuredOutput.schema)
            : undefined,
        }
      : undefined,
    middlewares: values.middlewares.length > 0
      ? values.middlewares.map((m) => ({ type: m.type, enabled: true, settings: m.settings }))
      : undefined,
    resilience: {
      maxRetries: values.resilience.maxRetries,
      initialDelayMs: values.resilience.initialDelayMs,
      backoffMultiplier: values.resilience.backoffMultiplier,
    },
    costBudget: values.budget.maxCostUsd > 0
      ? { maxCostUsd: values.budget.maxCostUsd }
      : undefined,
    skillRefs: values.skills.length > 0
      ? values.skills.map((skillId) => ({ skillId }))
      : undefined,
    metadata: Object.keys(values.metadata).length > 0 ? values.metadata : undefined,
  }
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
    const body = formToRequest(values)
    updateMutation.mutate(
      { id: id!, body },
      { onSuccess: () => navigate('/agents') },
    )
  }

  const tabItems = [
    { key: 'config', label: 'Configuracao' },
    { key: 'prompts', label: 'Prompts', badge: promptVersions?.length },
    { key: 'versions', label: 'Versoes' },
    { key: 'sandbox', label: 'Sandbox' },
  ]

  return (
    <div className="flex flex-col gap-4">
      {/* Header */}
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

      {/* Tabs */}
      <Tabs items={tabItems} active={activeTab} onChange={setActiveTab} />

      {/* Tab panels — all rendered, hidden with CSS to preserve state */}
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
    </div>
  )
}
