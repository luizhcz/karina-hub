export type {
  WorkflowDef,
  WorkflowEdge,
  WorkflowAgentRef,
  WorkflowTrigger,
  WorkflowConfiguration,
  CreateWorkflowRequest,
  WorkflowExecution,
  WorkflowValidationResult,
  WorkflowDiagram,
  SandboxResult,
  WorkflowVersion,
} from '../../api/workflows'

// ── Form-specific types ──────────────────────────────────────────────────────

export interface NodeHitlConfigForm {
  enabled: boolean
  when: 'before' | 'after'
  interactionType: 'Approval' | 'Input' | 'Choice'
  prompt: string
  showOutput: boolean
  options: string       // comma-separated, parsed on submit
  timeoutSeconds: number
}

export const HITL_DEFAULTS: NodeHitlConfigForm = {
  enabled: false,
  when: 'after',
  interactionType: 'Approval',
  prompt: '',
  showOutput: false,
  options: '',
  timeoutSeconds: 300,
}

export interface WorkflowAgentRefForm {
  agentId: string
  role: string
  hitl: NodeHitlConfigForm
}

export interface WorkflowEdgeCase {
  condition: string   // empty when isDefault
  target: string      // single node; serialized to targets: [target] on submit
  isDefault: boolean
}

export interface WorkflowEdgeForm {
  from: string
  to: string
  edgeType: 'Direct' | 'Conditional' | 'Switch' | 'FanOut' | 'FanIn'
  condition: string          // Conditional only
  cases: WorkflowEdgeCase[] // Switch only
  targets: string[]          // FanOut (destinations) & FanIn (sources)
  inputSource: string        // "" | "WorkflowInput"
}

export interface WorkflowExecutorForm {
  id: string
  functionName: string
  description: string
  hitl: NodeHitlConfigForm
}

export interface WorkflowFormValues {
  id: string
  name: string
  description: string
  orchestrationMode: string
  version: string
  agents: WorkflowAgentRefForm[]
  executors: WorkflowExecutorForm[]
  edges: WorkflowEdgeForm[]
  configuration: {
    maxRounds: number
    timeoutSeconds: number
    enableHumanInTheLoop: boolean
    checkpointMode: string
    exposeAsAgent: boolean
    inputMode: string
  }
  trigger: {
    type: 'OnDemand' | 'Scheduled' | 'EventDriven'
    cronExpression: string
    eventTopic: string
    enabled: boolean
  }
  metadata: { key: string; value: string }[]
}
