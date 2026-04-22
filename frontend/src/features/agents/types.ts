export type {
  AgentDef,
  AgentToolDef,
  AgentStructuredOutput,
  AgentMiddlewareConfig,
  CreateAgentRequest,
  AgentValidationResult,
  AgentVersion,
  SandboxResult,
  CompareResult,
} from '../../api/agents'

export type {
  AgentPromptVersion,
  SavePromptRequest,
  SetMasterResult,
} from '../../api/prompts'

export type {
  FunctionToolInfo,
} from '../../api/tools'

// ── Form-specific types ──────────────────────────────────────────────────────

export interface AgentFormValues {
  id: string
  name: string
  description: string
  metadata: Record<string, string>
  model: {
    deploymentName: string
    temperature: number
    maxTokens: number
  }
  provider: {
    type: string
    clientType: string
    endpoint: string
  }
  fallbackProvider: {
    enabled: boolean
    type: string
    endpoint: string
  }
  instructions: string
  /** Function tools selecionadas (nomes). Outros tipos — mcp, code_interpreter, etc — vivem em seus próprios campos. */
  tools: string[]
  /** Ids dos MCP servers referenciados por este agent (resolvidos live em runtime). */
  mcpServerIds: string[]
  skills: string[]
  structuredOutput: {
    responseFormat: string
    schemaName: string
    schemaDescription: string
    schema: string
  }
  middlewares: {
    type: string
    enabled: boolean
    settings: Record<string, string>
  }[]
  resilience: {
    maxRetries: number
    initialDelayMs: number
    backoffMultiplier: number
  }
  budget: {
    maxTokensPerExecution: number
    maxCostUsd: number
  }
}
