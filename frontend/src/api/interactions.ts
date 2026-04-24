import { get, post, ApiError } from './client'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'


export interface HumanInteraction {
  interactionId: string
  executionId: string
  workflowId: string
  prompt: string
  context?: string
  status: 'Pending' | 'Resolved' | 'Rejected'
  resolution?: string
  createdAt: string
  resolvedAt?: string
  /** UserId de quem resolveu a interação. "system:timeout" para expiração automática. Null em Pending. */
  resolvedBy?: string
}

/**
 * Shape do payload para `POST /api/interactions/{id}/resolve`.
 * Alinhado com `EfsAiHub.Host.Api.Models.Requests.ResolveInteractionRequest` no backend
 * (props `Resolution` / `Approved` — convertidas para camelCase pelo System.Text.Json).
 */
export interface ResolveInteractionRequest {
  resolution: string
  approved: boolean
}


export const KEYS = {
  pending: ['interactions', 'pending'] as const,
  detail: (id: string) => ['interactions', id] as const,
  byExecution: (executionId: string) => ['interactions', 'execution', executionId] as const,
}


export const getPendingInteractions = () => get<HumanInteraction[]>('/interactions/pending')
export const getInteraction = (id: string) => get<HumanInteraction>(`/interactions/${id}`)
export const resolveInteraction = (id: string, body: ResolveInteractionRequest) =>
  post<void>(`/interactions/${id}/resolve`, body)
export const getInteractionsByExecution = (executionId: string) =>
  get<HumanInteraction[]>(`/interactions/by-execution/${executionId}`)


export function usePendingInteractions() {
  return useQuery({ queryKey: KEYS.pending, queryFn: getPendingInteractions })
}

export function useInteraction(id: string, enabled = true) {
  return useQuery({ queryKey: KEYS.detail(id), queryFn: () => getInteraction(id), enabled })
}

export function useInteractionsByExecution(executionId: string, enabled = true) {
  return useQuery({
    queryKey: KEYS.byExecution(executionId),
    queryFn: () => getInteractionsByExecution(executionId),
    enabled,
  })
}

/**
 * Discrimina o motivo de uma falha em `resolveInteraction`. Backend retorna 404
 * quando o CAS a nível de banco perdeu (outro pod/caller já resolveu) OU quando
 * o ID não existe. UX trata iguais: "já foi resolvido".
 */
export function isHitlAlreadyResolvedError(err: unknown): err is ApiError {
  return err instanceof ApiError && err.status === 404
}

export function useResolveInteraction() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: ResolveInteractionRequest }) =>
      resolveInteraction(id, body),
    onSuccess: (_data, { id }) => {
      // Invalidar listas + detalhe da própria interação + listas by-execution
      // (pode haver uma tab aberta no ExecutionDetailPage mostrando HITLs da execução).
      qc.invalidateQueries({ queryKey: KEYS.pending })
      qc.invalidateQueries({ queryKey: KEYS.detail(id) })
      qc.invalidateQueries({ queryKey: ['interactions', 'execution'] })
    },
    onError: (err, { id }) => {
      // CAS perdido / 404: quem quer que esteja vendo, deve refetch pra mostrar resolução
      // feita por outro caller/pod sem duplicar side-effects.
      if (isHitlAlreadyResolvedError(err)) {
        qc.invalidateQueries({ queryKey: KEYS.pending })
        qc.invalidateQueries({ queryKey: KEYS.detail(id) })
        qc.invalidateQueries({ queryKey: ['interactions', 'execution'] })
      }
    },
  })
}
