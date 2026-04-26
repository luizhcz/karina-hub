import type { CreateWorkflowRequest, WorkflowEdge } from '../../api/workflows'
import { formToEdgePredicate } from './predicateUtils'
import type { WorkflowFormValues } from './types'

/**
 * Converte WorkflowFormValues (shape do react-hook-form) → CreateWorkflowRequest (API).
 * Centralizado em um único lugar pra Create e Edit pages compartilharem a mesma lógica
 * de serialização do EdgePredicate (paridade com EdgePredicateEditor).
 */
export function formValuesToWorkflowRequest(values: WorkflowFormValues): CreateWorkflowRequest {
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
    edges: values.edges.map(edgeFormToApi),
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

function edgeFormToApi(e: WorkflowFormValues['edges'][number]): WorkflowEdge {
  const inputSource = e.inputSource || undefined
  const handoffHint = e.handoffHint?.trim() ? e.handoffHint.trim() : undefined

  if (e.edgeType === 'Switch') {
    return {
      from: e.from || undefined,
      edgeType: e.edgeType,
      inputSource,
      cases: e.cases.map((c) => ({
        predicate: c.isDefault ? undefined : formToEdgePredicate(c.predicate),
        targets: c.target ? [c.target] : [],
        isDefault: c.isDefault,
      })),
    }
  }
  if (e.edgeType === 'Conditional') {
    return {
      from: e.from || undefined,
      to: e.to || undefined,
      edgeType: e.edgeType,
      predicate: formToEdgePredicate(e.predicate),
      inputSource,
    }
  }
  if (e.edgeType === 'FanOut') {
    return {
      from: e.from || undefined,
      edgeType: e.edgeType,
      targets: e.targets,
      inputSource,
    }
  }
  if (e.edgeType === 'FanIn') {
    return {
      to: e.to || undefined,
      edgeType: e.edgeType,
      sources: e.targets,
      inputSource,
    }
  }
  // Direct
  return {
    from: e.from || undefined,
    to: e.to || undefined,
    edgeType: e.edgeType,
    handoffHint,
    inputSource,
  }
}
