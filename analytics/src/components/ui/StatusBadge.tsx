/**
 * StatusBadge — pill com cor por status de execução.
 *
 * Reusabilidade: domain-agnostic. Recebe um status string e mapeia pra cor
 * via lookup. Telas que tenham outros enums de status (eval runs futuras,
 * standalone job status) reusam mudando só o lookup OU adicionando entry no
 * mesmo arquivo.
 *
 * NÃO faz: ícone, tooltip — caller decide se quer enriquecer.
 *
 * Exemplo: <StatusBadge status="Running" />
 *          Reusado por: tabela de Execuções (V1) + futuras telas com workflow status.
 */
import type { ReactNode } from 'react'
import { cn } from './cn'

interface StatusBadgeProps {
  status: string
  /** Override de classe extra (ex.: `text-xs` quando vai numa célula bem apertada). */
  className?: string
  children?: ReactNode
}

const STATUS_STYLES: Record<string, string> = {
  Running: 'bg-accent-subtle text-accent',
  Pending: 'bg-accent-subtle text-accent',
  Paused: 'bg-warning/15 text-warning',
  Completed: 'bg-success/15 text-success',
  Failed: 'bg-warning/15 text-warning',
  Cancelled: 'bg-surface-hover text-fg-dim',
}

// Fallback quando o status vem com nome desconhecido — neutro pra não
// classificar como falha por engano.
const FALLBACK_STYLE = 'bg-surface-hover text-fg-dim'

export function StatusBadge({ status, className, children }: StatusBadgeProps) {
  const style = STATUS_STYLES[status] ?? FALLBACK_STYLE
  return (
    <span
      className={cn(
        'inline-flex items-center rounded-md px-2 py-0.5 text-[11px] font-medium',
        style,
        className,
      )}
    >
      {children ?? status}
    </span>
  )
}
