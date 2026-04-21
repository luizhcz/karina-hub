import { cn } from '../../../shared/utils/cn'

export function StepBubble({ stepName, done }: { stepName: string; done: boolean }) {
  return (
    <div className="flex justify-center my-1">
      <span className="text-[10px] text-text-dimmed px-2 py-0.5 rounded-full border border-border-primary bg-bg-tertiary flex items-center gap-1.5">
        <span className={cn('w-1 h-1 rounded-full', done ? 'bg-green-500' : 'bg-accent-blue animate-pulse')} />
        {stepName}
      </span>
    </div>
  )
}
