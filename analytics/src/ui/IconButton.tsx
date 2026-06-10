import { forwardRef } from 'react'
import { cn } from './cn'

type Variant = 'ghost' | 'subtle' | 'danger'
type Size = 'sm' | 'md'

interface IconButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  /** label acessível obrigatório — ícones sozinhos precisam de aria-label */
  'aria-label': string
}

const variantClasses: Record<Variant, string> = {
  ghost:
    'text-fg-muted hover:bg-surface-hover hover:text-fg focus-visible:ring-accent/30',
  subtle:
    'border border-border bg-surface text-fg hover:bg-surface-hover focus-visible:ring-accent/30',
  danger:
    'text-fg-muted hover:bg-danger/10 hover:text-danger focus-visible:ring-danger/40',
}

const sizeClasses: Record<Size, string> = {
  sm: 'h-8 w-8 rounded-md',
  md: 'h-9 w-9 rounded-lg',
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton(
  { variant = 'ghost', size = 'md', className, disabled, children, type = 'button', ...rest },
  ref,
) {
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled}
      className={cn(
        'inline-flex items-center justify-center transition focus-visible:outline-none focus-visible:ring-2 disabled:cursor-not-allowed disabled:opacity-50',
        variantClasses[variant],
        sizeClasses[size],
        className,
      )}
      {...rest}
    >
      {children}
    </button>
  )
})
