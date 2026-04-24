/**
 * Utilitários de reconexão SSE para o chat AG-UI.
 *
 * Contexto: o endpoint /api/chat/ag-ui/stream é POST+SSE (streaming de resposta HTTP,
 * não EventSource nativo). Quando a conexão cai durante streaming (rede oscila,
 * backend reinicia), o backend expõe /api/chat/ag-ui/reconnect/{executionId}
 * que aceita header Last-Event-ID e retoma a partir do último evento entregue.
 *
 * Este módulo centraliza:
 *  - parsing de frames SSE (separados por \n\n, com `id:` e `data:`)
 *  - backoff exponencial com jitter (paridade com backend — ResiliencePolicy.JitterRatio)
 *  - loop de reconnect limitado por tentativas max
 */

export interface SseFrame {
  /** Valor de `id:` se presente no frame — usado como Last-Event-ID na próxima reconexão. */
  id: string | null
  /** Valor de `data:` concatenado (a especificação permite múltiplas linhas). */
  data: string
}

/** Política de reconexão. Defaults alinhados com o backend (ResiliencePolicy). */
export interface ReconnectPolicy {
  /** Máximo de tentativas de reconexão após a queda. Default: 5. */
  maxAttempts?: number
  /** Delay inicial em ms. Default: 500. */
  initialDelayMs?: number
  /** Multiplicador do backoff exponencial. Default: 2.0. */
  backoffMultiplier?: number
  /** Teto do delay em ms. Default: 30_000 (30s). */
  capDelayMs?: number
  /** Fração de jitter aleatório sobre o delay (0..1). Default: 0.1. */
  jitterRatio?: number
}

export const DEFAULT_RECONNECT_POLICY: Required<ReconnectPolicy> = {
  maxAttempts: 5,
  initialDelayMs: 500,
  backoffMultiplier: 2.0,
  capDelayMs: 30_000,
  jitterRatio: 0.1,
}

/**
 * Calcula o delay da tentativa `attempt` (0-indexed) aplicando backoff exponencial + jitter.
 * Respeita o teto `capDelayMs`. Equivalente ao ApplyJitter do RetryingChatClient.
 */
export function computeBackoffDelay(attempt: number, policy?: ReconnectPolicy): number {
  const p = { ...DEFAULT_RECONNECT_POLICY, ...policy }
  const base = p.initialDelayMs * Math.pow(p.backoffMultiplier, attempt)
  const capped = Math.min(base, p.capDelayMs)
  if (p.jitterRatio <= 0) return capped
  const jitter = Math.random() * capped * p.jitterRatio
  return capped + jitter
}

/**
 * Consome um `ReadableStreamDefaultReader<Uint8Array>` parseando frames SSE.
 * Invoca `onFrame` para cada frame com `data:` não-vazio.
 * Returns quando o stream termina (reader sinaliza done).
 *
 * Erros de leitura (rede caiu, backend crashed) são propagados como throw — caller
 * decide se reconecta via `streamWithReconnect`.
 */
export async function parseSseStream(
  reader: ReadableStreamDefaultReader<Uint8Array>,
  onFrame: (frame: SseFrame) => void,
): Promise<void> {
  const decoder = new TextDecoder()
  let buffer = ''

  for (;;) {
    const { done, value } = await reader.read()
    if (done) return

    buffer += decoder.decode(value, { stream: true })
    const frames = buffer.split('\n\n')
    buffer = frames.pop() ?? ''

    for (const raw of frames) {
      if (!raw.trim()) continue

      let id: string | null = null
      const dataLines: string[] = []
      for (const line of raw.split('\n')) {
        if (line.startsWith('id:')) id = line.slice(3).trim()
        else if (line.startsWith('data:')) dataLines.push(line.slice(5).trim())
      }
      if (dataLines.length === 0) continue
      onFrame({ id, data: dataLines.join('\n') })
    }
  }
}

export interface ReconnectState {
  attempt: number
  delayMs: number
}

/**
 * Dorme por `ms` milissegundos. Respeita AbortSignal (throw se abortar).
 */
export function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(resolve, ms)
    if (signal) {
      if (signal.aborted) {
        clearTimeout(timer)
        reject(new DOMException('Aborted', 'AbortError'))
        return
      }
      signal.addEventListener(
        'abort',
        () => {
          clearTimeout(timer)
          reject(new DOMException('Aborted', 'AbortError'))
        },
        { once: true },
      )
    }
  })
}

/**
 * Heurística: um throw/error durante `reader.read()` é retentável?
 * Retornamos true para qualquer erro de rede ou HTTP 5xx. Para 4xx (ex: 404 execução
 * inválida, 403 sem permissão), retornamos false pois reconectar não vai consertar.
 */
export function isRetriableStreamError(err: unknown): boolean {
  if (err instanceof DOMException && err.name === 'AbortError') return false
  if (!(err instanceof Error)) return true // erro desconhecido → tenta reconectar por segurança
  // Mensagens típicas: "Failed to fetch", "network error", "HTTP 502"
  const msg = err.message.toLowerCase()
  if (msg.includes('http 4')) return false // 4xx não retenta
  return true
}
