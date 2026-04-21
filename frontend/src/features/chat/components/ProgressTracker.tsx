import { cn } from '../../../shared/utils/cn'
import type { LocalMsg } from '../types'

type StepMsg = Extract<LocalMsg, { kind: 'step' }>

export function ProgressTracker({ steps }: { steps: StepMsg[] }) {
  return (
    <div className="flex justify-center my-2">
      <div className="bg-bg-tertiary border border-border-primary rounded-lg px-4 py-3 flex flex-col gap-0">
        {steps.map((step, i) => {
          const isLast = i === steps.length - 1
          const isActive = !step.done && isLast
          return (
            <div key={step.stepId} className="flex items-start gap-2.5">
              {/* Status circle + connector line */}
              <div className="flex flex-col items-center">
                <span
                  className={cn(
                    'w-2.5 h-2.5 rounded-full mt-0.5 flex-shrink-0',
                    step.done
                      ? 'bg-status-completed'
                      : isActive
                        ? 'bg-status-running animate-pulse'
                        : 'bg-border-secondary',
                  )}
                />
                {!isLast && (
                  <span className="w-px h-4 bg-border-secondary" />
                )}
              </div>

              {/* Label */}
              <span
                className={cn(
                  'text-[11px] leading-tight',
                  step.done
                    ? 'text-text-secondary'
                    : isActive
                      ? 'text-text-primary font-medium'
                      : 'text-text-muted',
                )}
              >
                {step.stepName}
              </span>
            </div>
          )
        })}
      </div>
    </div>
  )
}
