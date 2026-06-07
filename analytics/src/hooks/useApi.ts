// Hook genérico de fetch reativo a deps + retry exponencial em 5xx. Existe
// porque o brief proíbe React Query/SWR e queremos um único ponto que
// padronize: AbortController por chamada (cleanup), retry só pra erro
// transiente, expor refetch imperativo pra o usuário clicar "tentar de novo".
//
// Contrato:
//   - `fetcher` recebe um AbortSignal e DEVE encaminhá-lo pro fetch interno
//     (todas as funções de src/api aceitam RequestInit). Sem signal, mudar
//     dep dispara request novo mas o antigo fica vivo e pode setar estado
//     stale.
//   - `deps` segue regra de useEffect: shallow compare. Mudou → cancela
//     inflight e recomeça.
//   - `skip` (opcional): quando true, o hook não dispara o fetch — útil pra
//     guardas como "projectId ainda não foi selecionado" sem forçar o caller
//     a duplicar o estado de loading/data manualmente. Mudar de true → false
//     reinicia o ciclo normalmente.
//   - Retry: até 3 tentativas pra 5xx/network. Backoff 300ms, 900ms, 2.7s.
//     Nunca retry em 4xx (problema do cliente — repetir não muda).
//   - Abort não vira erro: o consumidor não vê erro de cancelamento.

import { useCallback, useEffect, useRef, useState } from 'react'
import { ApiError } from '../api/client'

export interface UseApiState<T> {
  data: T | null
  error: Error | null
  loading: boolean
  refetch: () => void
}

const MAX_ATTEMPTS = 3
const BASE_DELAY_MS = 300

function isAbortError(err: unknown): boolean {
  return err instanceof DOMException && err.name === 'AbortError'
}

function shouldRetry(err: unknown): boolean {
  if (err instanceof ApiError) return err.status >= 500
  // Erros de rede (fetch throw direto) não são ApiError — retry vale a pena.
  if (err instanceof TypeError) return true
  return false
}

function delay(ms: number, signal: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const t = setTimeout(resolve, ms)
    signal.addEventListener(
      'abort',
      () => {
        clearTimeout(t)
        reject(new DOMException('Aborted', 'AbortError'))
      },
      { once: true },
    )
  })
}

export interface UseApiOptions {
  /**
   * Quando true, o fetch não dispara e o estado fica em `{ data: null, error: null, loading: false }`.
   * Pré-condição típica: deps obrigatórias (projectId, agentId) ainda não foram preenchidas.
   * Mudar pra false reinicia o ciclo normalmente respeitando `deps`.
   */
  skip?: boolean
}

export function useApi<T>(
  fetcher: (signal: AbortSignal) => Promise<T>,
  deps: ReadonlyArray<unknown>,
  options: UseApiOptions = {},
): UseApiState<T> {
  const { skip = false } = options
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<Error | null>(null)
  // loading começa false quando skip=true — não há request em voo, então não
  // queremos pintar skeleton no caller.
  const [loading, setLoading] = useState(!skip)
  // tick imperativo pra refetch — bumpar força o useEffect a rodar de novo.
  const [tick, setTick] = useState(0)

  // Mantém o fetcher mais recente sem invalidar o useEffect (caller passa
  // arrow function inline; usar dep array do useEffect manda o controle).
  const fetcherRef = useRef(fetcher)
  fetcherRef.current = fetcher

  useEffect(() => {
    if (skip) {
      // Reset explícito: data fica null pra que caller saiba que "não há
      // dado válido pra esse skip atual". Loading=false porque não tem
      // request em voo. Error preservado seria confuso (de qual ciclo?).
      setData(null)
      setError(null)
      setLoading(false)
      return
    }

    const controller = new AbortController()
    const { signal } = controller
    let cancelled = false

    async function run() {
      setLoading(true)
      setError(null)
      for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
        try {
          const result = await fetcherRef.current(signal)
          if (cancelled || signal.aborted) return
          setData(result)
          setError(null)
          setLoading(false)
          return
        } catch (err) {
          if (cancelled || signal.aborted || isAbortError(err)) return
          if (attempt < MAX_ATTEMPTS && shouldRetry(err)) {
            // Backoff exponencial: 300ms, 900ms, 2.7s. Cap implícito por
            // MAX_ATTEMPTS — em 3 tentativas o ceiling é ~3s, dentro do
            // que um user tolera sem reclamar de tela "presa".
            try {
              await delay(BASE_DELAY_MS * Math.pow(3, attempt - 1), signal)
            } catch {
              return
            }
            continue
          }
          setError(err instanceof Error ? err : new Error(String(err)))
          setLoading(false)
          return
        }
      }
    }

    void run()
    return () => {
      cancelled = true
      controller.abort()
    }
    // deps + tick controlam a re-execução. skip entra no array pra que sair
    // de skip=true → skip=false dispare o fetch.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick, skip])

  const refetch = useCallback(() => setTick((t) => t + 1), [])

  return { data, error, loading, refetch }
}
