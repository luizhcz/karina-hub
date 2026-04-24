import { Link, useNavigate, useParams } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { Badge } from '../../shared/ui/Badge'
import { PageLoader } from '../../shared/ui/LoadingSpinner'
import { ErrorCard } from '../../shared/ui/ErrorCard'
import { useWorkflow, useUpdateWorkflow } from '../../api/workflows'
import type { CreateWorkflowRequest } from '../../api/workflows'
import { WorkflowForm } from './components/WorkflowForm'
import type { WorkflowFormValues } from './types'

function posNum(n: number): number | undefined {
  return Number.isFinite(n) && n > 0 ? n : undefined
}

function formToRequest(values: WorkflowFormValues): CreateWorkflowRequest {
  return {
    id: values.id,
    name: values.name,
    description: values.description || undefined,
    orchestrationMode: values.orchestrationMode,
    version: values.version || undefined,
    agents: values.agents.map((a) => ({
      agentId: a.agentId,
      role: a.role || undefined,
      hitl: a.hitl?.enabled
        ? {
            when: a.hitl.when,
            interactionType: a.hitl.interactionType,
            prompt: a.hitl.prompt,
            showOutput: a.hitl.showOutput,
            options: a.hitl.options
              ? a.hitl.options.split(',').map((o) => o.trim()).filter(Boolean)
              : undefined,
            timeoutSeconds: a.hitl.timeoutSeconds,
          }
        : undefined,
    })),
    executors:
      values.executors.length > 0
        ? values.executors.map((ex) => ({
            id: ex.id,
            functionName: ex.functionName,
            description: ex.description || undefined,
            hitl: ex.hitl?.enabled
              ? {
                  when: ex.hitl.when,
                  interactionType: ex.hitl.interactionType,
                  prompt: ex.hitl.prompt,
                  showOutput: ex.hitl.showOutput,
                  options: ex.hitl.options
                    ? ex.hitl.options.split(',').map((o) => o.trim()).filter(Boolean)
                    : undefined,
                  timeoutSeconds: ex.hitl.timeoutSeconds,
                }
              : undefined,
          }))
        : undefined,
    edges: values.edges.map((e) => {
      const inputSource = e.inputSource || undefined
      if (e.edgeType === 'Switch') {
        return {
          from: e.from || undefined,
          edgeType: e.edgeType,
          inputSource,
          cases: e.cases.map((c) => ({
            condition: c.isDefault ? undefined : c.condition || undefined,
            targets: c.target ? [c.target] : [],
            isDefault: c.isDefault,
          })),
        }
      }
      if (e.edgeType === 'FanOut') {
        return { from: e.from || undefined, edgeType: e.edgeType, targets: e.targets, inputSource }
      }
      if (e.edgeType === 'FanIn') {
        return { to: e.to || undefined, edgeType: e.edgeType, targets: e.targets, inputSource }
      }
      // Direct or Conditional
      return {
        from: e.from || undefined,
        to: e.to || undefined,
        edgeType: e.edgeType,
        condition: e.condition || undefined,
        inputSource,
      }
    }),
    configuration: {
      maxRounds: posNum(values.configuration.maxRounds),
      timeoutSeconds: posNum(values.configuration.timeoutSeconds),
      enableHumanInTheLoop: values.configuration.enableHumanInTheLoop,
      checkpointMode: values.configuration.checkpointMode,
      exposeAsAgent: values.configuration.exposeAsAgent,
      inputMode: values.configuration.inputMode,
    },
    trigger:
      values.trigger.type !== 'OnDemand'
        ? {
            type: values.trigger.type,
            cronExpression:
              values.trigger.type === 'Scheduled'
                ? values.trigger.cronExpression || undefined
                : undefined,
            eventTopic:
              values.trigger.type === 'EventDriven'
                ? values.trigger.eventTopic || undefined
                : undefined,
            enabled: values.trigger.enabled,
          }
        : {
            type: 'OnDemand',
            enabled: values.trigger.enabled,
          },
    metadata:
      values.metadata.length > 0
        ? Object.fromEntries(
            values.metadata.filter((m) => m.key).map((m) => [m.key, m.value]),
          )
        : undefined,
  }
}

export function WorkflowEditPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data: workflow, isLoading, error, refetch } = useWorkflow(id!, !!id)
  const updateMutation = useUpdateWorkflow()

  if (isLoading) return <PageLoader />
  if (error || !workflow)
    return <ErrorCard message="Erro ao carregar workflow." onRetry={refetch} />

  const handleSubmit = (values: WorkflowFormValues) => {
    const body = formToRequest(values)
    updateMutation.mutate(
      { id: id!, body },
      { onSuccess: () => navigate('/workflows') },
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3 flex-wrap">
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            &larr; Workflows
          </Button>
        </Link>
        <div className="flex-1">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-2xl font-bold text-text-primary">
              Editar: {workflow.name}
            </h1>
            <Badge variant="blue">{workflow.orchestrationMode}</Badge>
            {workflow.version && (
              <Badge variant="gray">v{workflow.version}</Badge>
            )}
          </div>
          <p className="text-sm text-text-muted mt-1">
            ID: <code className="font-mono text-xs text-text-secondary">{workflow.id}</code>
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/diagram`)}
          >
            Diagram
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/versions`)}
          >
            Versions
          </Button>
          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate(`/workflows/${id}/trigger`)}
          >
            Trigger
          </Button>
          {workflow.configuration?.inputMode?.toLowerCase() === 'chat' ? (
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate('/chat')}
            >
              💬 Testar no Chat
            </Button>
          ) : (
            <Button
              variant="secondary"
              size="sm"
              onClick={() => navigate(`/workflows/${id}/sandbox`)}
            >
              Sandbox
            </Button>
          )}
        </div>
      </div>

      <WorkflowForm
        initialValues={workflow}
        onSubmit={handleSubmit}
        loading={updateMutation.isPending}
        isEdit
      />

      {updateMutation.error && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg px-4 py-3 text-sm text-red-400">
          Erro ao salvar workflow: {(updateMutation.error as Error).message}
        </div>
      )}
    </div>
  )
}
