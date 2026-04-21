import { cn } from '../../../shared/utils/cn'

export function ToolCallBubble({ toolName, done }: { toolName: string; done: boolean }) {
  return (
    <div className="flex justify-start">
      <div className="px-3 py-1.5 rounded-lg text-xs text-text-muted bg-bg-tertiary border border-border-primary flex items-center gap-2">
        <span className={cn('w-1.5 h-1.5 rounded-full flex-shrink-0', done ? 'bg-green-500' : 'bg-accent-blue animate-pulse')} />
        {done ? `✓ ${toolName}` : `Chamando ${toolName}…`}
      </div>
    </div>
  )
}
