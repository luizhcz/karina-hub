import { post, get } from './client'

/**
 * Cliente do workflow `wf-gerador-testcases` (agent gerador-testcases no
 * projeto geral, Visibility=global). Recebe a definição do agente alvo e
 * devolve N test cases sintéticos cobrindo happy-path, edge-case, rejection
 * e tool-validation.
 */

export interface GeneratorAgent {
  agentName: string
  description?: string
  instructions?: string
  tools?: { name: string; type?: string }[]
}

export interface GeneratorInput extends GeneratorAgent {
  /** Quantidade alvo (5/10/15). Backend respeita, com tolerância pra qualidade. */
  targetCaseCount: number
}

export interface GeneratedCase {
  input: string
  expectedOutput: string
  expectedToolCalls: string[]
  tags: string[]
  weight: number
}

export interface GeneratorOutput {
  testCases: GeneratedCase[]
  summary: string
  quantity: number
}

export class GeneratorTimeoutError extends Error {
  constructor() {
    super('O gerador demorou demais para responder. Tente reduzir a quantidade ou refinar a descrição do agente.')
    this.name = 'GeneratorTimeoutError'
  }
}

export class GeneratorFailureError extends Error {
  constructor(message?: string) {
    super(message ?? 'O gerador não conseguiu produzir uma suíte agora. Tente novamente em instantes.')
    this.name = 'GeneratorFailureError'
  }
}

const WORKFLOW_ID = 'wf-gerador-testcases'

const POLL_DELAYS_MS = [600, 800, 1000, 1500, 2000] as const
const DEADLINE_MS = 60_000

interface TriggerResponse {
  executionId: string
  statusUrl?: string
}

interface ExecutionResponse {
  executionId: string
  workflowId: string
  status: string
  output: string | null
}

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

/**
 * Dispara `wf-gerador-testcases` e aguarda o output estruturado via polling
 * (backoff 600→2000ms, deadline 60s). O workflow está hardcoded com timeout
 * 60s no seed; deadline aqui combina.
 */
export async function generateCases(
  input: GeneratorInput,
  signal?: AbortSignal,
): Promise<GeneratorOutput> {
  const trigger = await post<TriggerResponse>(`/workflows/${WORKFLOW_ID}/trigger`, {
    input: JSON.stringify(input),
    metadata: { source: 'test-set-editor' },
  })

  const start = Date.now()
  let attempt = 0
  while (true) {
    if (signal?.aborted) throw new DOMException('Aborted', 'AbortError')
    if (Date.now() - start > DEADLINE_MS) throw new GeneratorTimeoutError()

    await sleep(nextDelay(attempt), signal)
    attempt++

    const exec = await get<ExecutionResponse>(`/executions/${trigger.executionId}`)

    if (exec.status === 'Completed') {
      const raw = exec.output ?? ''
      try {
        return JSON.parse(raw) as GeneratorOutput
      } catch {
        throw new GeneratorFailureError('Resposta do gerador fora do formato esperado.')
      }
    }
    if (exec.status === 'Failed' || exec.status === 'Cancelled') {
      throw new GeneratorFailureError()
    }
  }
}
