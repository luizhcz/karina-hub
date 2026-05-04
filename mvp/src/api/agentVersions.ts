import { get } from './client'

export interface AgentModelSnapshot {
  deploymentName: string
  temperature?: number | null
  maxTokens?: number | null
  [key: string]: unknown
}

export interface AgentProviderSnapshot {
  type: string
  clientType: string
  endpoint?: string | null
  [key: string]: unknown
}

export interface AgentToolSnapshot {
  type: string
  name?: string | null
  [key: string]: unknown
}

export interface AgentMiddlewareSnapshot {
  type: string
  enabled: boolean
  settings?: Record<string, string>
}

export interface AgentStructuredOutputSnapshot {
  responseFormat?: string | null
  schemaName?: string | null
  schemaDescription?: string | null
  schemaJson?: string | null
}

export interface AgentVersion {
  agentVersionId: string
  agentDefinitionId: string
  revision: number
  status: string
  createdAt: string
  createdBy?: string | null
  changeReason?: string | null
  promptContent?: string | null
  promptVersionId?: string | null
  model?: AgentModelSnapshot | null
  provider?: AgentProviderSnapshot | null
  fallbackProvider?: AgentProviderSnapshot | null
  tools?: AgentToolSnapshot[]
  middlewarePipeline?: AgentMiddlewareSnapshot[]
  outputSchema?: AgentStructuredOutputSnapshot | null
  resilience?: unknown
  costBudget?: unknown
  skillRefs?: unknown[]
  contentHash: string
  description?: string | null
  metadata?: Record<string, string> | null
  breakingChange: boolean
}

export const listAgentVersions = (agentId: string) =>
  get<AgentVersion[]>(`/agents/${agentId}/versions`)

export const getAgentVersion = (agentId: string, versionId: string) =>
  get<AgentVersion>(`/agents/${agentId}/versions/${versionId}`)
