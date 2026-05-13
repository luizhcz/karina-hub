import { post, get, ApiError } from './client'

export type Severidade = 'alta' | 'media' | 'baixa'
export type SugestaoTipo = 'lacuna' | 'melhoria' | 'risco'

/**
 * Sugestão de refinamento do agente assistente-perfil. O LLM aponta a seção
 * alvo (ou `null` quando a sugestão é global/transversal) e fornece o
 * `exemplo` em texto. O frontend decide se aplica como Substituir ou Mesclar
 * via toggle no AssistantDiff — o LLM nunca decide a operação.
 *
 * `secao` é o nome exato do header `## ` no markdown do user (case-insensitive
 * + trim). Quando o header não existe no doc atual, a UI sinaliza isso e
 * Substituir vira "criar seção nova".
 */
export interface Sugestao {
  secao: string | null
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
  /** Markdown integral do perfil, do jeito que o user escreveu no editor. */
  profile: string
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

const HEADER_REGEX = /^#{1,2}\s+(.+?)\s*$/gm

/**
 * Extrai os nomes das seções (`## Header`) na ordem em que aparecem no
 * markdown. Usado para ordenar os grupos do drawer de forma estável — grupos
 * de sugestões aparecem na mesma ordem do texto do user; seção `null` no fim
 * como grupo "Geral".
 */
function sectionsInOrder(markdown: string): string[] {
  HEADER_REGEX.lastIndex = 0
  const result: string[] = []
  const seen = new Set<string>()
  const matches = markdown.matchAll(HEADER_REGEX)
  for (const m of matches) {
    const raw = m[1].trim()
    const key = raw.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)
    result.push(raw)
  }
  return result
}

/**
 * Agrupa as sugestões por `secao` na ordem em que as seções aparecem no
 * markdown atual. Sugestões cuja `secao` não bate com nenhuma seção do doc
 * (case-insensitive) vão pro fim, depois das que batem; sugestões com
 * `secao: null` vão por último em um grupo "Geral" (chave null no Map).
 */
export function groupBySecao(
  sugestoes: Sugestao[],
  profileMarkdown: string,
): Map<string | null, Sugestao[]> {
  const map = new Map<string | null, Sugestao[]>()
  const orderedKeys: (string | null)[] = []
  const pushTo = (key: string | null, s: Sugestao) => {
    const existing = map.get(key)
    if (existing) {
      existing.push(s)
      return
    }
    map.set(key, [s])
    orderedKeys.push(key)
  }

  // Primeira passada: distribui em buckets indexados por toLowerCase().
  const byLower = new Map<string, Sugestao[]>()
  const nullBucket: Sugestao[] = []
  const orphanBuckets: Map<string, Sugestao[]> = new Map()
  const sections = sectionsInOrder(profileMarkdown)
  const lowerToCanonical = new Map<string, string>()
  for (const s of sections) lowerToCanonical.set(s.toLowerCase(), s)

  for (const s of sugestoes) {
    if (s.secao === null) {
      nullBucket.push(s)
      continue
    }
    const lower = s.secao.trim().toLowerCase()
    if (lowerToCanonical.has(lower)) {
      const list = byLower.get(lower) ?? []
      list.push(s)
      byLower.set(lower, list)
    } else {
      const list = orphanBuckets.get(lower) ?? []
      list.push(s)
      orphanBuckets.set(lower, list)
    }
  }

  // Ordem: seções na ordem do doc.
  for (const sec of sections) {
    const lower = sec.toLowerCase()
    const list = byLower.get(lower)
    if (!list) continue
    for (const s of list) pushTo(sec, s)
  }
  // Depois: órfãs (seções que o LLM sugeriu mas o user não tem no doc).
  // Preserva a primeira capitalização vista nas sugestões.
  for (const [lower, list] of orphanBuckets) {
    const canonical = list[0]?.secao?.trim() ?? lower
    for (const s of list) pushTo(canonical, s)
  }
  // Por fim: sugestões globais.
  for (const s of nullBucket) pushTo(null, s)

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
