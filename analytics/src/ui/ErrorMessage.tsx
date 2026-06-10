import { cn } from './cn'

interface ErrorMessageProps {
  message: string
  className?: string
}

export function ErrorMessage({ message, className }: ErrorMessageProps) {
  return (
    <div
      role="alert"
      className={cn(
        'rounded-lg border border-danger/40 bg-danger/10 px-4 py-3 text-sm text-danger',
        className,
      )}
    >
      {message}
    </div>
  )
}
