import { CheckIcon, cn } from '../../ui'
import type { StepKey } from './types'

export interface StepDescriptor {
  key: StepKey
  label: string
}

interface StepperProps {
  steps: StepDescriptor[]
  current: StepKey
  onSelect: (key: StepKey) => void
  disabled?: boolean
  // Steps com pendência obrigatória — ganham um indicador warning na pílula
  // pra que o user identifique no Stepper (não só no Review) o que falta.
  issues?: Partial<Record<StepKey, string>>
}

export function Stepper({ steps, current, onSelect, disabled, issues }: StepperProps) {
  const currentIndex = steps.findIndex((s) => s.key === current)

  return (
    <nav className="flex items-center gap-2" aria-label="Progresso">
      {steps.map((step, idx) => {
        const isCurrent = step.key === current
        const isDone = idx < currentIndex
        const issueMsg = issues?.[step.key]
        const hasIssue = !!issueMsg
        return (
          <div key={step.key} className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => !disabled && onSelect(step.key)}
              disabled={disabled}
              aria-label={issueMsg ? `${step.label} — ${issueMsg}` : step.label}
              className={cn(
                'flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-medium transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
                disabled && 'cursor-not-allowed opacity-60',
                !disabled && 'hover:border-accent/40',
                isCurrent
                  ? hasIssue
                    ? 'border-warning bg-warning text-white shadow-soft'
                    : 'border-accent bg-accent text-accent-contrast shadow-soft'
                  : hasIssue
                    ? 'border-warning/50 bg-warning/10 text-warning'
                    : isDone
                      ? 'border-success/40 bg-success/10 text-success'
                      : 'border-border bg-surface text-fg-muted',
              )}
            >
              <span
                className={cn(
                  'flex h-5 w-5 items-center justify-center rounded-full text-[10px] font-bold',
                  isCurrent
                    ? hasIssue
                      ? 'bg-white/20 text-white'
                      : 'bg-accent-contrast/20 text-accent-contrast'
                    : hasIssue
                      ? 'bg-warning/20 text-warning'
                      : isDone
                        ? 'bg-success/20 text-success'
                        : 'bg-bg-soft text-fg-dim',
                )}
              >
                {hasIssue ? '!' : isDone ? <CheckIcon className="h-3 w-3" /> : idx + 1}
              </span>
              {step.label}
            </button>
            {idx < steps.length - 1 && (
              <span
                aria-hidden="true"
                className={cn(
                  'h-px w-6 transition',
                  idx < currentIndex ? 'bg-success/40' : 'bg-border',
                )}
              />
            )}
          </div>
        )
      })}
    </nav>
  )
}
