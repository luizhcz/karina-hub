export interface AgentToolDef {
  type: string
  name?: string
  requiresApproval?: boolean
  serverLabel?: string
  serverUrl?: string
  allowedTools?: string[]
  requireApproval?: string
  headers?: Record<string, string>
  connectionId?: string
}

export interface AgentStructuredOutput {
  responseFormat: string
  schemaName?: string
  schemaDescription?: string
  schema?: unknown
}

export interface AgentMiddlewareConfig {
  type: string
  enabled: boolean
  settings?: Record<string, string>
}

export interface AgentDef {
  id: string
  name: string
  description?: string
  model: { deploymentName: string; temperature?: number; maxTokens?: number }
  provider?: { type?: string; clientType?: string; endpoint?: string }
  instructions?: string
  tools?: AgentToolDef[]
  structuredOutput?: AgentStructuredOutput
  middlewares?: AgentMiddlewareConfig[]
  metadata?: Record<string, string>
  createdAt?: string
  updatedAt?: string
}

export interface CreateAgentRequest {
  id: string
  name: string
  description?: string
  model: { deploymentName: string; temperature?: number; maxTokens?: number }
  provider?: { type?: string; clientType?: string; endpoint?: string }
  instructions?: string
  tools?: AgentToolDef[]
  structuredOutput?: AgentStructuredOutput
  middlewares?: AgentMiddlewareConfig[]
  metadata?: Record<string, string>
}

// ── Available Functions types ─────────────────────────────────────────────────

export interface FunctionToolInfo {
  name: string
  description?: string
}

export interface CodeExecutorInfo {
  name: string
}

export interface AvailableFunctions {
  functionTools: FunctionToolInfo[]
  codeExecutors: CodeExecutorInfo[]
}

export interface WorkflowEdge {
  from?: string
  to?: string
  edgeType: 'Direct' | 'Conditional' | 'Switch' | 'FanOut' | 'FanIn'
  condition?: string
  targets?: string[]
  cases?: { condition?: string; targets: string[]; isDefault: boolean }[]
}

export interface WorkflowExecutorStep {
  id: string
  functionName: string
  description?: string
}

export interface WorkflowAgentRef {
  agentId: string
  role?: string
  handoffCondition?: string
}

export interface WorkflowTrigger {
  type: 'OnDemand' | 'Scheduled' | 'EventDriven'
  cronExpression?: string
  eventTopic?: string
  enabled?: boolean
  disableAfterFire?: boolean
  lastFiredAt?: string
}

export interface WorkflowConfiguration {
  maxRounds?: number
  timeoutSeconds?: number
  enableHumanInTheLoop?: boolean
  checkpointMode?: string
  exposeAsAgent?: boolean
  exposedAgentDescription?: string
  inputMode?: string
  maxHistoryMessages?: number
  maxAgentInvocations?: number
}

export interface WorkflowDef {
  id: string
  name: string
  description?: string
  version?: string
  orchestrationMode: string
  agents: WorkflowAgentRef[]
  executors?: WorkflowExecutorStep[]
  edges: WorkflowEdge[]
  trigger?: WorkflowTrigger
  configuration?: WorkflowConfiguration
  metadata?: Record<string, string>
  createdAt?: string
  updatedAt?: string
}

export interface CreateWorkflowRequest {
  id: string
  name: string
  description?: string
  version?: string
  orchestrationMode: string
  agents: WorkflowAgentRef[]
  executors?: WorkflowExecutorStep[]
  edges?: WorkflowEdge[]
  trigger?: WorkflowTrigger
  configuration?: WorkflowConfiguration
  metadata?: Record<string, string>
}

export interface WorkflowExecution {
  executionId: string
  workflowId: string
  status: 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled' | 'Paused'
  input?: string
  output?: string
  errorMessage?: string
  startedAt: string
  completedAt?: string
  metadata?: Record<string, string>
}

export interface NodeRecord {
  nodeId: string
  executionId: string
  nodeType: 'agent' | 'executor' | 'trigger'
  status: 'pending' | 'running' | 'completed' | 'failed'
  startedAt?: string
  completedAt?: string
  output?: string
  iteration: number
  tokensUsed?: number
}

export interface ExecutionEventRecord {
  eventType: string
  executionId: string
  payload: string
  timestamp: string
}

export interface SSEEvent {
  type: string
  payload: Record<string, unknown>
}

// ── Chat types ────────────────────────────────────────────────────────────────

export interface ConversationSession {
  conversationId: string
  userId: string
  userType?: string
  workflowId: string
  title?: string
  activeExecutionId?: string
  contextClearedAt?: string
  createdAt: string
  lastMessageAt: string
  metadata?: Record<string, string>
}

export interface Boleta {
  order_type: 'Buy' | 'Sell'
  ticker: string
  account: string
  quantity: string
  priceLimit: string
  priceType: 'M' | 'L' | 'F'
  volume: string
  expireTime: string
}

export interface OutputBoleta {
  boletas: Boleta[]
  message: string
  command: string
  ui_component: 'none' | 'order_card' | 'incomplete_card' | 'help_card' | 'status_success' | 'status_error' | 'out_of_scope'
}

export interface PosicaoCliente {
  ticker: string
  totalQuantity: number
  financialVolume: number
}

export interface OutputRelatorio {
  message: string
  posicoes: PosicaoCliente[]
  ui_component: 'position_report' | 'position_empty'
}

export type StructuredOutput = OutputBoleta | OutputRelatorio

export interface ChatMsg {
  messageId: string
  conversationId: string
  role: 'user' | 'assistant' | 'system'
  message: string
  output?: StructuredOutput | null
  createdAt: string
  executionId?: string
}

export interface ChatSendResult {
  executionId?: string
  hitlResolved: boolean
  messageIds?: string[]
}

// ── Token Usage types ────────────────────────────────────────────────────────

export interface LlmTokenUsage {
  id: number
  agentId: string
  modelId: string
  executionId?: string
  workflowId?: string
  inputTokens: number
  outputTokens: number
  totalTokens: number
  durationMs: number
  promptVersionId?: string
  outputContent?: string
  createdAt: string
}

export interface AgentTokenSummary {
  agentId: string
  modelId: string
  totalInput: number
  totalOutput: number
  totalTokens: number
  callCount: number
  avgDurationMs: number
}

export interface GlobalTokenSummary {
  totalInput: number
  totalOutput: number
  totalTokens: number
  totalCalls: number
  avgDurationMs: number
  byAgent: AgentTokenSummary[]
}

export interface ThroughputBucket {
  bucket: string
  executions: number
  tokens: number
  llmCalls: number
  avgDurationMs: number
}

export interface ThroughputResult {
  buckets: ThroughputBucket[]
  avgExecutionsPerHour: number
  avgTokensPerHour: number
  avgCallsPerHour: number
}

// ── Tool Invocation types ─────────────────────────────────────────────

export interface ToolInvocation {
  id: number
  executionId: string
  agentId: string
  toolName: string
  arguments?: string
  result?: string
  durationMs: number
  success: boolean
  errorMessage?: string
  createdAt: string
}

// ── Model Pricing types ───────────────────────────────────────────────

export interface ModelPricing {
  id: number
  modelId: string
  provider: string
  pricePerInputToken: number
  pricePerOutputToken: number
  currency: string
  effectiveFrom: string
  effectiveTo?: string
}

// ── Monitor Dashboard types ───────────────────────────────────────────

// ── Prompt Versioning types ──────────────────────────────────────────

export interface AgentPromptVersion {
  versionId: string
  content: string
  isActive: boolean
}

export type MonitorTab = 'operational' | 'agents' | 'tools' | 'ux' | 'troubleshooting' | 'outputs' | 'testcases'
export type TimeRange = '1h' | '24h' | '7d' | '30d'

// ── Analytics types ───────────────────────────────────────────────────

export interface ExecutionSummary {
  total: number
  completed: number
  failed: number
  cancelled: number
  running: number
  pending: number
  successRate: number
  avgDurationMs: number
  p50Ms: number
  p95Ms: number
}

export interface ExecutionTimeseriesBucket {
  bucket: string
  total: number
  completed: number
  failed: number
  avgDurationMs: number
}

export interface ExecutionTimeseries {
  buckets: ExecutionTimeseriesBucket[]
}

// ── HITL Interaction types ────────────────────────────────────────────

export interface HumanInteraction {
  interactionId: string
  executionId: string
  workflowId: string
  prompt: string
  context?: string
  status: 'Pending' | 'Resolved' | 'Rejected'
  resolution?: string
  createdAt: string
  resolvedAt?: string
}

// ── Model Pricing create request ──────────────────────────────────────

export interface CreateModelPricingRequest {
  modelId: string
  provider: string
  pricePerInputToken: number
  pricePerOutputToken: number
  currency?: string
  effectiveFrom: string
}

// ── Alert System types ────────────────────────────────────────────────

export type AlertSeverity = 'WARNING' | 'CRITICAL'

export interface MonitorAlert {
  id: string
  severity: AlertSeverity
  title: string
  message: string
  triggeredAt: number
  value: number
  threshold: number
}
