import { forwardRef } from 'react'
import { cn } from './cn'
import { Spinner } from './Spinner'

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
  // Primary: chapado por padrão (parece mais profissional banking-style),
  // shadow só no hover pra dar feedback de elevação.
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
      {loading ? <Spinner className="h-4 w-4" /> : leftIcon}
      {children}
      {!loading && rightIcon}
    </button>
  )
})
