import { cn } from './cn'

type Tone = 'neutral' | 'accent' | 'success' | 'warning' | 'danger' | 'method-get' | 'method-post'

interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  tone?: Tone
  /** versão sólida vs sutil; default = subtle */
  variant?: 'subtle' | 'solid'
}

const subtleClasses: Record<Tone, string> = {
  neutral: 'bg-bg-soft text-fg-muted border-border',
  accent: 'bg-accent-subtle text-accent border-accent/30',
  success: 'bg-success/10 text-success border-success/30',
  warning: 'bg-warning/10 text-warning border-warning/30',
  danger: 'bg-danger/10 text-danger border-danger/30',
  'method-get': 'bg-success/10 text-success border-success/30',
  'method-post': 'bg-warning/10 text-warning border-warning/30',
}

const solidClasses: Record<Tone, string> = {
  neutral: 'bg-fg-muted text-bg border-transparent',
  accent: 'bg-accent text-accent-contrast border-transparent',
  success: 'bg-success text-white border-transparent',
  warning: 'bg-warning text-white border-transparent',
  danger: 'bg-danger text-white border-transparent',
  'method-get': 'bg-success text-white border-transparent',
  'method-post': 'bg-warning text-white border-transparent',
}

export function Badge({
  tone = 'neutral',
  variant = 'subtle',
  className,
  children,
  ...rest
}: BadgeProps) {
  return (
    <span
      className={cn(
        'inline-flex items-center gap-1 rounded-md border px-2 py-0.5 text-[11px] font-semibold',
        variant === 'solid' ? solidClasses[tone] : subtleClasses[tone],
        className,
      )}
      {...rest}
    >
      {children}
    </span>
  )
}
