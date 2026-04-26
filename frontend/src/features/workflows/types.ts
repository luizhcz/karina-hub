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
  EdgeOperator,
  EdgePredicate,
  EdgePredicateValueType,
  WorkflowSwitchCase,
} from '../../api/workflows'


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

/**
 * Forma do predicate no formulário. Mantém todos os campos como strings/booleans
 * para compatibilidade com react-hook-form; serialização final pra EdgePredicate
 * acontece no submit (parseValue por valueType).
 */
export interface EdgePredicateForm {
  path: string
  operator:
    | 'Eq'
    | 'NotEq'
    | 'Gt'
    | 'Gte'
    | 'Lt'
    | 'Lte'
    | 'Contains'
    | 'StartsWith'
    | 'EndsWith'
    | 'MatchesRegex'
    | 'In'
    | 'NotIn'
    | 'IsNull'
    | 'IsNotNull'
  /** Conteúdo digitado/selecionado pelo usuário; vazio é válido em IsNull/IsNotNull. */
  valueRaw: string
  valueType: 'Auto' | 'String' | 'Number' | 'Integer' | 'Boolean' | 'Enum'
}

export const EDGE_PREDICATE_DEFAULTS: EdgePredicateForm = {
  path: '',
  operator: 'Eq',
  valueRaw: '',
  valueType: 'String',
}

export interface WorkflowEdgeCase {
  predicate: EdgePredicateForm
  target: string
  isDefault: boolean
}

export interface WorkflowEdgeForm {
  from: string
  to: string
  edgeType: 'Direct' | 'Conditional' | 'Switch' | 'FanOut' | 'FanIn'
  predicate: EdgePredicateForm   // Conditional only (zerado nos demais)
  cases: WorkflowEdgeCase[]      // Switch only
  targets: string[]              // FanOut (destinations) & FanIn (sources)
  handoffHint: string            // Modo Handoff (opcional)
  inputSource: string            // "" | "WorkflowInput"
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
