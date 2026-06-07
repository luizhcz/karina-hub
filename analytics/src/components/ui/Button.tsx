/**
 * Button — variantes padrão pra todos os CTAs do app.
 *
 * Reusabilidade: domain-agnostic. Apenas estética + estados (loading,
 * disabled). Não conhece roteamento — se precisar de link, envolva com
 * <Link> do react-router (`<Link><Button as=... /></Link>` ou variante
 * separada futura).
 *
 * NÃO faz: navegação, submit-on-enter de forms (use type="submit"),
 * confirmação de ação destrutiva (responsabilidade do caller).
 *
 * Exemplo: <Button variant="primary" onClick={refetch}>Tentar de novo</Button>
 *          Reusado por: ErrorState, futuros forms de filtro avançado.
 */
import { forwardRef } from 'react'
import { cn } from './cn'

type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md' | 'lg'

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  loading?: boolean
  leftIcon?: React.ReactNode
  rightIcon?: React.ReactNode
}

const variantClasses: Record<Variant, string> = {
  primary:
    'bg-accent text-accent-contrast hover:bg-accent-soft hover:shadow-soft focus-visible:ring-accent/40',
  secondary:
    'border border-border bg-surface text-fg hover:bg-surface-hover focus-visible:ring-accent/30',
  ghost:
    'text-fg-muted hover:bg-surface-hover hover:text-fg focus-visible:ring-accent/30',
  danger:
    'bg-danger text-white hover:opacity-90 focus-visible:ring-danger/40',
}

const sizeClasses: Record<Size, string> = {
  sm: 'h-8 rounded-md px-3 text-xs gap-1.5',
  md: 'h-9 rounded-lg px-4 text-sm gap-2',
  lg: 'h-11 rounded-lg px-5 text-sm gap-2',
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    variant = 'primary',
    size = 'md',
    loading = false,
    leftIcon,
    rightIcon,
    className,
    disabled,
    children,
    type = 'button',
    ...rest
  },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      className={cn(
        'inline-flex items-center justify-center font-medium transition focus-visible:outline-none focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-50',
        variantClasses[variant],
        sizeClasses[size],
        className,
      )}
      {...rest}
    >
      {loading ? (
        <svg className="h-4 w-4 animate-spin text-current" viewBox="0 0 24 24" fill="none">
          <circle cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" className="opacity-20" />
          <path d="M22 12a10 10 0 0 1-10 10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
        </svg>
      ) : (
        leftIcon
      )}
      {children}
      {!loading && rightIcon}
    </button>
  )
})
