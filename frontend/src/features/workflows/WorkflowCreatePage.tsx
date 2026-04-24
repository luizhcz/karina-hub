import { Link, useNavigate } from 'react-router'
import { Button } from '../../shared/ui/Button'
import { useCreateWorkflow } from '../../api/workflows'
import type { CreateWorkflowRequest } from '../../api/workflows'
import { WorkflowForm } from './components/WorkflowForm'
import type { WorkflowFormValues } from './types'

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
      maxRounds: values.configuration.maxRounds || undefined,
      timeoutSeconds: values.configuration.timeoutSeconds || undefined,
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

export function WorkflowCreatePage() {
  const navigate = useNavigate()
  const createMutation = useCreateWorkflow()

  const handleSubmit = (values: WorkflowFormValues) => {
    const body = formToRequest(values)
    createMutation.mutate(body, {
      onSuccess: () => navigate('/workflows'),
    })
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/workflows">
          <Button variant="ghost" size="sm">
            &larr; Workflows
          </Button>
        </Link>
        <div>
          <h1 className="text-2xl font-bold text-text-primary">Novo Workflow</h1>
          <p className="text-sm text-text-muted mt-1">
            Configure um novo workflow de orquestração de agentes.
          </p>
        </div>
      </div>

      <WorkflowForm onSubmit={handleSubmit} loading={createMutation.isPending} />

      {createMutation.error && (
        <div className="bg-red-500/10 border border-red-500/30 rounded-lg px-4 py-3 text-sm text-red-400">
          Erro ao criar workflow: {(createMutation.error as Error).message}
        </div>
      )}
    </div>
  )
}
