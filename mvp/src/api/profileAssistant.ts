import { post, get, ApiError } from './client'

export type Severidade = 'alta' | 'media' | 'baixa'
export type SugestaoTipo = 'lacuna' | 'melhoria' | 'risco'

export type CampoPerfil =
  | 'name'
  | 'description'
  | 'role'
  | 'goal'
  | 'backstory'
  | 'rules'
  | 'constraints'

export interface Sugestao {
  campo: CampoPerfil
  tipo: SugestaoTipo
  severidade: Severidade
  mensagem: string
  exemplo: string | null
}

export interface RefinamentoPerfilOutput {
  sugestoes: Sugestao[]
  resumo: string
  score: number
}

export interface ProfileInput {
  name: string
  role: string
  goal: string
  backstory: string
  rules: string
  constraints: string
}

interface TriggerResponse {
  executionId: string
  statusUrl?: string
}

interface ExecutionResponse {
  executionId: string
  workflowId: string
  status: string
  output: string | null
  startedAt?: string
  completedAt?: string | null
}

const WORKFLOW_ID = 'wf-assistente-perfil'

const POLL_DELAYS_MS = [400, 600, 900, 1350, 1500] as const
const DEADLINE_MS = 28_000

function nextDelay(attempt: number): number {
  const idx = Math.min(attempt, POLL_DELAYS_MS.length - 1)
  return POLL_DELAYS_MS[idx]
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    if (signal?.aborted) {
      reject(new DOMException('Aborted', 'AbortError'))
      return
    }
    const t = setTimeout(() => {
      signal?.removeEventListener('abort', onAbort)
      resolve()
    }, ms)
    const onAbort = () => {
      clearTimeout(t)
      reject(new DOMException('Aborted', 'AbortError'))
    }
    signal?.addEventListener('abort', onAbort, { once: true })
  })
}

export class AssistantTimeoutError extends Error {
  constructor() {
    super('O assistente demorou demais para responder.')
    this.name = 'AssistantTimeoutError'
  }
}

export class AssistantFailureError extends Error {
  constructor(message?: string) {
    super(message ?? 'O assistente não conseguiu concluir a análise.')
    this.name = 'AssistantFailureError'
  }
}

/**
 * Dispara o workflow `wf-assistente-perfil` e aguarda o output estruturado
 * via polling (backoff 400→1500ms, deadline 28s — workflow timeout é 30s).
 */
export async function analisarPerfil(
  input: ProfileInput,
  signal?: AbortSignal,
): Promise<RefinamentoPerfilOutput> {
  const trigger = await post<TriggerResponse>(`/workflows/${WORKFLOW_ID}/trigger`, {
    input: JSON.stringify(input),
    metadata: { source: 'agent-editor' },
  })

  const start = Date.now()
  let attempt = 0

  while (true) {
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
    if (Date.now() - start > DEADLINE_MS) throw new AssistantTimeoutError()

    await sleep(nextDelay(attempt), signal)
    attempt++

    let exec: ExecutionResponse
    try {
      exec = await get<ExecutionResponse>(`/executions/${trigger.executionId}`)
    } catch (err) {
      if (err instanceof ApiError && err.status === 404) continue
      throw err
    }

    if (exec.status === 'Completed') {
      const raw = exec.output ?? ''
      try {
        const parsed = JSON.parse(raw) as RefinamentoPerfilOutput
        return parsed
      } catch {
        throw new AssistantFailureError('Resposta do assistente fora do formato esperado.')
      }
    }
    if (exec.status === 'Failed' || exec.status === 'Cancelled') {
      throw new AssistantFailureError()
    }
  }
}

const FIELD_ORDER: CampoPerfil[] = [
  'name',
  'description',
  'role',
  'goal',
  'backstory',
  'rules',
  'constraints',
]

export function groupByCampo(sugestoes: Sugestao[]): Map<CampoPerfil, Sugestao[]> {
  const map = new Map<CampoPerfil, Sugestao[]>()
  for (const campo of FIELD_ORDER) map.set(campo, [])
  for (const s of sugestoes) {
    const list = map.get(s.campo)
    if (list) list.push(s)
  }
  for (const key of Array.from(map.keys())) {
    if (map.get(key)!.length === 0) map.delete(key)
  }
  return map
}

export function severityRank(s: Severidade): number {
  return s === 'alta' ? 0 : s === 'media' ? 1 : 2
}

export function countCriticas(sugestoes: Sugestao[]): number {
  return sugestoes.filter((s) => s.severidade === 'alta').length
}

/** Hash estável (FNV-1a 32) dos campos pra detectar input idêntico no cooldown. */
export function hashInput(input: ProfileInput): string {
  const s = JSON.stringify(input)
  let h = 0x811c9dc5
  for (let i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i)
    h = (h + ((h << 1) + (h << 4) + (h << 7) + (h << 8) + (h << 24))) >>> 0
  }
  return h.toString(16)
}
