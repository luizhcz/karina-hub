import { get, post, put, patch, del } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export interface AgentToolDef {
  type: string
  name?: string
  requiresApproval?: boolean
  /**
   * Id-based reference (preferido) para MCP tools — aponta para registro em
   * /api/admin/mcp-servers. Quando presente, serverLabel/serverUrl/allowedTools/headers
   * são resolvidos em runtime pelo backend (mudanças no registry propagam).
   */
  mcpServerId?: string
  /** Legacy/fallback (BC): campo inline de MCP — preferir mcpServerId. */
  serverLabel?: string
  /** Legacy/fallback (BC): campo inline de MCP. */
  serverUrl?: string
  /** Legacy/fallback (BC): campo inline de MCP. */
  allowedTools?: string[]
  requireApproval?: string
  /** Legacy/fallback (BC): headers inline de MCP. */
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

export interface AgentResiliencePolicy {
  maxRetries?: number
  initialDelayMs?: number
  backoffMultiplier?: number
}

export interface AgentCostBudget {
  maxCostUsd: number
}

export interface AgentSkillRef {
  skillId: string
  skillVersionId?: string
}

export type AgentVisibility = 'project' | 'global'

export interface AgentDef {
  id: string
  name: string
  description?: string
  model: { deploymentName: string; temperature?: number; maxTokens?: number }
  provider?: { type?: string; clientType?: string; endpoint?: string }
  fallbackProvider?: { type?: string; endpoint?: string }
  instructions?: string
  tools?: AgentToolDef[]
  structuredOutput?: AgentStructuredOutput
  middlewares?: AgentMiddlewareConfig[]
  resilience?: AgentResiliencePolicy
  costBudget?: AgentCostBudget
  skillRefs?: AgentSkillRef[]
  metadata?: Record<string, string>
  /** "project" (default) | "global". Global agents visíveis em todos os projetos do tenant. */
  visibility?: AgentVisibility
  /** Project owner do agent. */
  originProjectId?: string
  /** Tenant do owner. */
  originTenantId?: string
  /** Whitelist opcional de projetos autorizados (apenas com visibility=global). null = qualquer projeto do tenant. */
  allowedProjectIds?: string[] | null
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
  resilience?: AgentResiliencePolicy
  costBudget?: AgentCostBudget
  skillRefs?: AgentSkillRef[]
  metadata?: Record<string, string>
  /** Opcional. Default "project" em Create; preserved em Update (PATCH /visibility é o caminho). */
  visibility?: AgentVisibility
  /** Whitelist opcional. */
  allowedProjectIds?: string[] | null
}

export interface AgentValidationResult {
  isValid: boolean
  errors: string[]
}

export interface AgentVersion {
  versionId: string
  agentId: string
  createdAt: string
  description?: string
}

/**
 * Resposta detalhada de version (POST /api/agents/{id}/versions).
 * Subset dos campos do AgentVersionResponse backend — apenas os usados pela UI
 * (toast com versionId+revision, badge de breaking). Snapshots completos
 * (Model, Provider, Tools, etc) são consumidos via outros endpoints.
 */
export interface AgentVersionDetail {
  agentVersionId: string
  agentDefinitionId: string
  revision: number
  createdAt: string
  createdBy?: string | null
  changeReason?: string | null
  status: string
  contentHash: string
  /** true=breaking; false=patch (default). */
  breakingChange: boolean
}

export interface SandboxResult {
  output: string
  success: boolean
  durationMs: number
}

export interface CompareResult {
  differences: { field: string; before: unknown; after: unknown }[]
}


export const KEYS = {
  all: ['agents'] as const,
  detail: (id: string) => ['agents', id] as const,
  versions: (id: string) => ['agents', id, 'versions'] as const,
  version: (id: string, vid: string) => ['agents', id, 'versions', vid] as const,
}


export const getAgents = () => get<AgentDef[]>('/agents')
export const getAgent = (id: string) => get<AgentDef>(`/agents/${id}`)
export const createAgent = (body: CreateAgentRequest) => post<AgentDef>('/agents', body)
export const updateAgent = (id: string, body: CreateAgentRequest) => put<AgentDef>(`/agents/${id}`, body)
export const deleteAgent = (id: string) => del(`/agents/${id}`)
export const validateAgent = (id: string) => post<AgentValidationResult>(`/agents/${id}/validate`)
export const getAgentVersions = (id: string) => get<AgentVersion[]>(`/agents/${id}/versions`)
export const getAgentVersion = (id: string, vid: string) => get<AgentDef>(`/agents/${id}/versions/${vid}`)
export const rollbackAgent = (id: string, body?: { versionId?: string }) => post<AgentDef>(`/agents/${id}/rollback`, body)
export const publishAgentVersion = (id: string, body: { breakingChange: boolean; changeReason?: string }) =>
  post<AgentVersionDetail>(`/agents/${id}/versions`, body)
export const updateAgentVisibility = (id: string, body: { visibility: AgentVisibility; reason?: string }) =>
  patch<AgentDef>(`/agents/${id}/visibility`, body)
export const sandboxAgent = (id: string, body: { input: string }) => post<SandboxResult>(`/agents/${id}/sandbox`, body)
export const compareAgent = (id: string, body: { versionA: string; versionB: string }) =>
  post<CompareResult>(`/agents/${id}/compare`, body)


export function useAgents() {
  return useQuery({ queryKey: KEYS.all, queryFn: getAgents })
}

export function useAgent(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getAgent(id), enabled })
}

export function useAgentVersions(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.versions(id), queryFn: () => getAgentVersions(id), enabled })
}

export function useAgentVersion(id: string, vid: string, enabled = true) {
  return useQuery({ queryKey: KEYS.version(id, vid), queryFn: () => getAgentVersion(id, vid), enabled })
}

export function useCreateAgent() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: createAgent,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useUpdateAgent() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: CreateAgentRequest }) => updateAgent(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useDeleteAgent() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: deleteAgent,
    onSuccess: () => { qc.invalidateQueries({ queryKey: KEYS.all }) },
  })
}

export function useUpdateAgentVisibility() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, visibility, reason }: { id: string; visibility: AgentVisibility; reason?: string }) =>
      updateAgentVisibility(id, { visibility, reason }),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.all })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
    },
  })
}

export function useValidateAgent() {
  return useMutation({ mutationFn: validateAgent })
}

export function useRollbackAgent() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body?: { versionId?: string } }) => rollbackAgent(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: KEYS.versions(id) })
    },
  })
}

export function usePublishAgentVersion() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: {
      id: string
      body: { breakingChange: boolean; changeReason?: string }
    }) => publishAgentVersion(id, body),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: KEYS.versions(id) })
    },
  })
}

export function useSandboxAgent() {
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { input: string } }) => sandboxAgent(id, body),
  })
}

export function useCompareAgent() {
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: { versionA: string; versionB: string } }) => compareAgent(id, body),
  })
}
