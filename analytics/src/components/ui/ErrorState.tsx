/**
 * ErrorState — exibição de erro com CTA de retry. Aceita Error ou string
 * pra que o caller possa passar o resultado direto do `useApi` sem unwrap.
 *
 * Reusabilidade: domain-agnostic. Não decide se um erro merece retry — quem
 * sabe é o useApi (que já tentou 3x em 5xx). Aqui o botão dispara refetch
 * pra recomeçar o ciclo; em 4xx, a UX é menos útil mas não é nociva.
 *
 * Tradução: usa friendlyError pra mapear ApiError em mensagem amigável;
 * `Error` comum vai pra mensagem direta.
 *
 * NÃO faz: redirect, logout (responsabilidade da camada de auth).
 *
 * Exemplo: error && <ErrorState error={error} onRetry={refetch} />
 *          Reusado por: todo card que usa useApi.
 */
import { friendlyError } from '../../api/client'
import { Button } from './Button'

interface ErrorStateProps {
  error: Error | string
  onRetry?: () => void
  /** Texto do botão. Default: "Tentar de novo". */
  retryLabel?: string
}

export function ErrorState({ error, onRetry, retryLabel = 'Tentar de novo' }: ErrorStateProps) {
  const message = typeof error === 'string' ? error : friendlyError(error)
  return (
    <div
      role="alert"
      className="flex flex-col items-center gap-3 px-6 py-10 text-center"
    >
      <div className="flex h-10 w-10 items-center justify-center rounded-full bg-danger/10 text-danger">
        <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <circle cx="12" cy="12" r="10" />
          <line x1="12" y1="8" x2="12" y2="12" />
          <line x1="12" y1="16" x2="12.01" y2="16" />
        </svg>
      </div>
      <div>
        <p className="text-sm font-medium text-fg">Não conseguimos carregar.</p>
        <p className="mt-1 text-xs text-fg-muted">{message}</p>
      </div>
      {onRetry && (
        <Button variant="secondary" size="sm" onClick={onRetry}>
          {retryLabel}
        </Button>
      )}
    </div>
  )
}
