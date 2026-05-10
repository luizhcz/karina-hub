import { del, get, post, put } from './client'

// Tipos espelham EfsAiHub.Core.Agents.RouterIntent (camelCase via API). Pool
// global por tenant — todos os projetos do tenant enxergam tudo. ProjectId no
// registro é a "categoria"; UI faz lookup pelo nome via mvp/src/api/projects.ts.

export interface RouterIntent {
  id: string
  tenantId: string
  /** Categoria = projeto. Persistido como id; nome resolvido por lookup local. */
  projectId: string
  /** Nome canônico (snake_case) usado pelo runtime do Router. Decidido pelo analyzer. */
  name: string
  /** Texto humano editável (PT-BR). Quando ausente, UI cai pro `name`. */
  displayName?: string | null
  description: string
  examples: string[]
  createdAt: string
  updatedAt: string
}

export interface CreateRouterIntentBody {
  /** Opcional. Se ausente, backend gera GUID. */
  id?: string
  /** Snake_case canônico. Quando ausente, backend deriva do displayName. */
  name?: string
  displayName: string
  description: string
  /** Quando ausente, backend usa o projeto atual do contexto. */
  projectId?: string
  examples?: string[]
}

export interface UpdateRouterIntentBody {
  /** Quando ausente, preserva o name canônico atual. */
  name?: string
  displayName: string
  description: string
  /** Quando ausente, preserva o projectId atual. */
  projectId?: string
  examples?: string[]
}

export interface RouterIntentUsage {
  agentId: string
  agentName: string
  projectId: string
}

/** Heurísticas de qualidade da descrição reportadas pelo analyzer (LLM). */
export interface RouterIntentQualityChecks {
  contextoUsuario: boolean
  foraDeEscopo: boolean
  resultadoEsperado: boolean
}

/** Output do agente analyzer (workflow wf-router-intent-analyzer). */
export interface RouterIntentAnalyzerOutput {
  conflictFound: boolean
  conflictsWith: string[]
  details: string
  /** Snake_case canônico — vai pro `name` no save. */
  suggestedName: string
  /** PT-BR humano — vai pro `displayName` no save. */
  suggestedDisplayName: string
  suggestedProjectId: string
  qualityChecks: RouterIntentQualityChecks
}

/** Endpoint de analyze retorna apenas o executionId; UI faz polling. */
export interface AnalyzeRouterIntentResult {
  executionId: string
}

const BASE = '/router-intents'

export const listRouterIntents = () => get<RouterIntent[]>(BASE)

export const getRouterIntent = (id: string) => get<RouterIntent>(`${BASE}/${id}`)

export const createRouterIntent = (body: CreateRouterIntentBody) =>
  post<RouterIntent>(BASE, body)

export const updateRouterIntent = (id: string, body: UpdateRouterIntentBody) =>
  put<RouterIntent>(`${BASE}/${id}`, body)

export const deleteRouterIntent = (id: string) =>
  del<void>(`${BASE}/${id}`)

export const getRouterIntentUsage = (id: string) =>
  get<RouterIntentUsage[]>(`${BASE}/${id}/usage`)

/**
 * Dispara o agente analyzer. Backend retorna executionId; UI polla
 * /executions/{id} até Completed e parseia o output via schema.
 */
export const analyzeRouterIntent = (body: {
  description: string
  examples?: string[]
  nameHint?: string
  displayNameHint?: string
  projectIdHint?: string
  excludeId?: string
}) => post<AnalyzeRouterIntentResult>(`${BASE}/analyze`, body)
