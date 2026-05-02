import { get, post, put, apiUrl, getIdentityHeaders } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { useUserStore } from '../stores/user'

export type EvaluatorKind = 'Local' | 'Meai' | 'Foundry'
export type SplitterStrategy = 'LastTurn' | 'Full' | 'PerTurn'
export type RunStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled'
export type TriggerSource = 'Manual' | 'AgentVersionPublished' | 'ApiClient'
export type VersionStatus = 'Draft' | 'Published' | 'Deprecated'
export type Visibility = 'project' | 'global'

export interface CatalogEntry {
  kind: EvaluatorKind
  name: string
  dimension: string
  description: string
  requiresParams: boolean
  paramsExampleJson?: string
}

export interface EvaluatorBinding {
  kind: EvaluatorKind
  name: string
  params?: unknown
  enabled: boolean
  weight: number
  bindingIndex: number
}

export interface EvaluatorConfig {
  id: string
  agentDefinitionId: string
  name: string
  currentVersionId?: string
  createdAt: string
  updatedAt: string
  createdBy?: string
}

export interface EvaluatorConfigVersion {
  evaluatorConfigVersionId: string
  evaluatorConfigId: string
  revision: number
  status: VersionStatus
  contentHash: string
  bindings: EvaluatorBinding[]
  splitter: SplitterStrategy
  numRepetitions: number
  createdAt: string
  createdBy?: string
  changeReason?: string
}

/**
 * Resposta de GET /api/agents/{id}/evaluator-config — agente sem config configurado
 * retorna ambos campos null/undefined (200 OK), pra UI renderizar form vazio sem 404 noise.
 */
export interface EvaluatorConfigWithVersion {
  config?: EvaluatorConfig | null
  currentVersion?: EvaluatorConfigVersion | null
}

export interface RegressionConfig {
  agentDefinitionId: string
  regressionTestSetId?: string
  regressionEvaluatorConfigVersionId?: string
  autotriggerEnabled: boolean
}

export interface TestSet {
  id: string
  projectId: string
  name: string
  description?: string
  visibility: Visibility
  currentVersionId?: string
  createdAt: string
  updatedAt: string
  createdBy?: string
}

export interface TestSetVersion {
  testSetVersionId: string
  testSetId: string
  revision: number
  status: VersionStatus
  contentHash: string
  createdAt: string
  createdBy?: string
  changeReason?: string
}

export interface TestSetWithVersions {
  testSet: TestSet
  versions: TestSetVersion[]
}

export interface TestCase {
  caseId: string
  index: number
  input: string
  expectedOutput?: string
  expectedToolCalls?: unknown
  tags: string[]
  weight: number
  createdAt: string
}

export interface CreateTestSetRequest {
  name: string
  description?: string
  visibility: Visibility
}

export interface PublishTestSetVersionRequest {
  cases: Array<{
    input: string
    expectedOutput?: string
    expectedToolCalls?: unknown
    tags?: string[]
    weight?: number
  }>
  changeReason?: string
}

export interface UpsertEvaluatorConfigRequest {
  name: string
  bindings: EvaluatorBinding[]
  splitter: SplitterStrategy
  numRepetitions: number
  changeReason?: string
}

export interface UpsertRegressionConfigRequest {
  regressionTestSetId?: string | null
  regressionEvaluatorConfigVersionId?: string | null
}

export interface CreateRunRequest {
  testSetVersionId: string
  evaluatorConfigVersionId: string
  agentVersionId?: string
}

export interface EnqueueRunResponse {
  runId?: string
  status?: RunStatus
  skipped: boolean
  skipReason?: string
  deduplicatedFromExisting: boolean
}

export interface EvaluationRun {
  runId: string
  projectId: string
  agentDefinitionId: string
  agentVersionId: string
  testSetVersionId: string
  evaluatorConfigVersionId: string
  baselineRunId?: string
  status: RunStatus
  priority: number
  triggeredBy?: string
  triggerSource: TriggerSource
  triggerContext?: unknown
  casesTotal: number
  startedAt?: string
  completedAt?: string
  lastError?: string
  createdAt: string
  casesCompleted: number
  casesPassed: number
  casesFailed: number
  avgScore?: number
  totalCostUsd: number
  totalTokens: number
}

export interface EvaluationResult {
  resultId: string
  caseId: string
  evaluatorName: string
  bindingIndex: number
  repetitionIndex: number
  score?: number
  passed: boolean
  reason?: string
  outputContent?: string
  judgeModel?: string
  latencyMs?: number
  costUsd?: number
  inputTokens?: number
  outputTokens?: number
  createdAt: string
}

export interface CompareResponse {
  runIdA: string
  runIdB: string
  passRateA?: number
  passRateB?: number
  passRateDelta?: number
  casesFailedA: number
  casesFailedB: number
  casesFailedDelta: number
  regressionDetected: boolean
  caseDiffs: Array<{
    caseId: string
    passedA?: boolean
    passedB?: boolean
    scoreA?: number
    scoreB?: number
    reasonA?: string
    reasonB?: string
  }>
}

export function useEvaluatorCatalog() {
  return useQuery<CatalogEntry[]>({
    queryKey: ['evaluator-catalog'],
    queryFn: () => get<CatalogEntry[]>('/evaluator-config/catalog'),
    staleTime: 60 * 60 * 1000,
  })
}

export function useTestSets(projectId: string, includeGlobal = true, enabled = true) {
  return useQuery<TestSet[]>({
    queryKey: ['testsets', projectId, includeGlobal],
    queryFn: () => get<TestSet[]>(`/projects/${projectId}/evaluation-test-sets`, { includeGlobal }),
    enabled,
  })
}

export function useTestSet(id: string, enabled = true) {
  return useQuery<TestSetWithVersions>({
    queryKey: ['testset', id],
    queryFn: () => get<TestSetWithVersions>(`/evaluation-test-sets/${id}`),
    enabled,
  })
}

export function useTestSetVersionCases(versionId: string, enabled = true) {
  return useQuery<TestCase[]>({
    queryKey: ['testset-version-cases', versionId],
    queryFn: () => get<TestCase[]>(`/evaluation-test-sets/versions/${versionId}/cases`),
    enabled,
  })
}

export function useCreateTestSet() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ projectId, body }: { projectId: string; body: CreateTestSetRequest }) =>
      post<TestSet>(`/projects/${projectId}/evaluation-test-sets`, body),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['testsets', vars.projectId] }),
  })
}

export function usePublishTestSetVersion() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: PublishTestSetVersionRequest }) =>
      post<TestSetVersion>(`/evaluation-test-sets/${id}/versions`, body),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['testset', vars.id] }),
  })
}

export function useImportTestSetCsv() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, file, changeReason }: { id: string; file: File; changeReason?: string }) => {
      const formData = new FormData()
      formData.append('file', file)
      if (changeReason) formData.append('changeReason', changeReason)
      const res = await fetch(apiUrl(`/evaluation-test-sets/${id}/versions/import`), {
        method: 'POST',
        headers: getIdentityHeaders(),
        body: formData,
      })
      if (!res.ok) {
        let msg = `HTTP ${res.status}`
        try {
          const body = (await res.json()) as { error?: string }
          if (body?.error) msg = body.error
        } catch { /* fallback */ }
        throw new Error(msg)
      }
      return (await res.json()) as TestSetVersion
    },
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['testset', vars.id] }),
  })
}

export function useUpdateTestSetVersionStatus() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, vid, status }: { id: string; vid: string; status: VersionStatus }) =>
      put<void>(`/evaluation-test-sets/${id}/versions/${vid}/status`, { status }),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['testset', vars.id] }),
  })
}

export function useEvaluatorConfig(agentId: string, enabled = true) {
  return useQuery<EvaluatorConfigWithVersion>({
    queryKey: ['evaluator-config', agentId],
    queryFn: () => get<EvaluatorConfigWithVersion>(`/agents/${agentId}/evaluator-config`),
    enabled,
  })
}

export function useEvaluatorConfigHistory(agentId: string, enabled = true) {
  return useQuery<EvaluatorConfigVersion[]>({
    queryKey: ['evaluator-config-history', agentId],
    queryFn: () => get<EvaluatorConfigVersion[]>(`/agents/${agentId}/evaluator-config/history`),
    enabled,
  })
}

export function useUpsertEvaluatorConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: string; body: UpsertEvaluatorConfigRequest }) =>
      put<EvaluatorConfigVersion>(`/agents/${agentId}/evaluator-config`, body),
    onSuccess: (_, vars) => {
      qc.invalidateQueries({ queryKey: ['evaluator-config', vars.agentId] })
      qc.invalidateQueries({ queryKey: ['evaluator-config-history', vars.agentId] })
    },
  })
}

export function useRegressionConfig(agentId: string, enabled = true) {
  return useQuery<RegressionConfig>({
    queryKey: ['regression-config', agentId],
    queryFn: () => get<RegressionConfig>(`/agents/${agentId}/regression-config`),
    enabled,
  })
}

export function useUpdateRegressionConfig() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: string; body: UpsertRegressionConfigRequest }) =>
      put<RegressionConfig>(`/agents/${agentId}/regression-config`, body),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['regression-config', vars.agentId] }),
  })
}

export function useAgentRuns(
  agentId: string,
  opts?: { skip?: number; take?: number; triggerSource?: TriggerSource },
  enabled = true,
) {
  return useQuery<EvaluationRun[]>({
    queryKey: ['agent-runs', agentId, opts?.skip, opts?.take, opts?.triggerSource],
    queryFn: () => get<EvaluationRun[]>(`/agents/${agentId}/evaluations/runs`, opts),
    enabled,
  })
}

export function useRun(runId: string, enabled = true) {
  return useQuery<EvaluationRun>({
    queryKey: ['run', runId],
    queryFn: () => get<EvaluationRun>(`/evaluations/runs/${runId}`),
    enabled,
  })
}

export function useRunResults(
  runId: string,
  opts?: { passed?: boolean; evaluator?: string; skip?: number; take?: number },
  enabled = true,
) {
  return useQuery<EvaluationResult[]>({
    queryKey: ['run-results', runId, opts?.passed, opts?.evaluator, opts?.skip, opts?.take],
    queryFn: () => get<EvaluationResult[]>(`/evaluations/runs/${runId}/results`, opts),
    enabled,
  })
}

export function useEnqueueRun() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ agentId, body }: { agentId: string; body: CreateRunRequest }) =>
      post<EnqueueRunResponse>(`/agents/${agentId}/evaluations/runs`, body),
    onSuccess: (_, vars) => qc.invalidateQueries({ queryKey: ['agent-runs', vars.agentId] }),
  })
}

export function useCancelRun() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (runId: string) => post<void>(`/evaluations/runs/${runId}/cancel`),
    onSuccess: (_, runId) => {
      qc.invalidateQueries({ queryKey: ['run', runId] })
      qc.invalidateQueries({ queryKey: ['agent-runs'] })
    },
  })
}

export function useCompareRuns(runA?: string, runB?: string) {
  return useQuery<CompareResponse>({
    queryKey: ['runs-compare', runA, runB],
    queryFn: () => get<CompareResponse>('/evaluations/runs/compare', { runA, runB }),
    enabled: !!runA && !!runB,
  })
}

export function exportRunUrl(runId: string, format: 'csv' | 'json' = 'csv'): string {
  return apiUrl(`/evaluations/runs/${runId}/export?format=${format}`)
}

// EventSource não suporta headers customizados (limitação do browser),
// então projectId e identidade (account/userProfileId) viajam como query params.
export function streamRunUrl(runId: string, projectId?: string): string {
  const base = apiUrl(`/evaluations/runs/${runId}/stream`)
  const params = new URLSearchParams()

  if (projectId && projectId !== 'default') {
    params.set('projectId', projectId)
  }

  const { userId, userType } = useUserStore.getState()
  if (userId) {
    params.set(userType === 'cliente' ? 'account' : 'userProfileId', userId)
  }

  const qs = params.toString()
  return qs ? `${base}?${qs}` : base
}
