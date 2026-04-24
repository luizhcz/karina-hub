import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useAgent, useUpdateAgent } from '../../api/agents'
import { AgentForm } from './components/AgentForm'
import type { AgentFormValues } from './types'
import type { CreateAgentRequest } from '../../api/agents'

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
    tools: (() => {
      const merged = [
        ...values.tools.map((name) => ({ type: 'function', name })),
        ...values.mcpServerIds.map((mcpServerId) => ({ type: 'mcp', mcpServerId })),
      ]
      return merged.length > 0 ? merged : undefined
    })(),
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

export function AgentEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: agent, isLoading, error, refetch } = useAgent(id!, !!id)
  const updateMutation = useUpdateAgent()

  if (isLoading) return <PageLoader />
  if (error || !agent) return <ErrorCard message="Erro ao carregar agente." onRetry={refetch} />

  const handleSubmit = (values: AgentFormValues) => {
    const body = formToRequest(values)
    updateMutation.mutate(
      { id: id!, body },
      { onSuccess: () => navigate('/agents') },
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/agents">
          <Button variant="ghost" size="sm">
            &larr; Agentes
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Editar: {agent.name}</h1>
          <p className="text-sm text-text-muted mt-1">Altere os campos e salve.</p>
        </div>
      </div>

      <AgentForm
        initialValues={agent}
        onSubmit={handleSubmit}
        loading={updateMutation.isPending}
      />
    </div>
  )
}
