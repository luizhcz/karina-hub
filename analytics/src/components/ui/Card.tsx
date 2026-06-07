/**
 * Card — painel padrão pra agrupar conteúdo (KPI, gráfico, tabela).
 *
 * Reusabilidade: domain-agnostic. Não assume métrica, contém só layout
 * (borda + raio + sombra + slots de header/body). Variant `error` muda só
 * cor da borda — usar quando o painel inteiro tá num estado de falha
 * (ex.: budget endpoint retornou 5xx mas overview foi). Para erro inline,
 * use ErrorState dentro do body.
 *
 * NÃO faz: padding interno do body, scroll, sticky header. Quem precisa
 * desses comportamentos passa via className.
 *
 * Exemplo: <Card><CardHeader title="Custo MTD" /><div>...</div></Card>
 *          Reusado por: Overview, Custos, futuramente Reliability, LLM calls.
 */
import { forwardRef } from 'react'
import { cn } from './cn'

type CardVariant = 'default' | 'error'

interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  /** Quando false, remove padding interno. Útil pra embutir Table edge-to-edge. */
  padded?: boolean
  /** Hover lift — sinaliza que o card é clicável. */
  interactive?: boolean
  variant?: CardVariant
}

export const Card = forwardRef<HTMLDivElement, CardProps>(function Card(
  { padded = true, interactive = false, variant = 'default', className, children, ...rest },
  ref,
) {
  return (
    <div
      ref={ref}
      className={cn(
        'rounded-2xl border bg-surface shadow-sm',
        variant === 'error' ? 'border-danger/40' : 'border-border',
        padded && 'p-5',
        interactive &&
          'transition-all duration-200 hover:-translate-y-0.5 hover:border-accent/40 hover:shadow-md',
        className,
      )}
      {...rest}
    >
      {children}
    </div>
  )
})

interface CardHeaderProps {
  title: React.ReactNode
  description?: React.ReactNode
  /** Slot pra ações (botão, dropdown). Posicionado à direita do título. */
  actions?: React.ReactNode
  className?: string
}

export function CardHeader({ title, description, actions, className }: CardHeaderProps) {
  return (
    <div className={cn('flex items-start justify-between gap-3', className)}>
      <div className="min-w-0">
        <h3 className="text-sm font-semibold text-fg">{title}</h3>
        {description && <p className="mt-1 text-xs text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </div>
  )
}
