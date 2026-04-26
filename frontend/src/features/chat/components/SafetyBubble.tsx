interface Props {
  category?: string
  message: string
  violationId?: string
}

export function SafetyBubble({ category, message, violationId }: Props) {
  return (
    <div className="flex justify-start" role="alert" aria-live="polite">
      <div className="max-w-[70%] px-4 py-3 rounded-2xl rounded-bl-sm text-sm bg-amber-500/10 border border-amber-500/30 text-amber-300">
        <div className="flex items-center gap-2 font-medium mb-1">
          <span aria-hidden="true">🚫</span>
          <span>Política do projeto</span>
          {category && (
            <>
              <span className="sr-only">Categoria:</span>
              <span
                className="text-xs px-1.5 py-0.5 rounded bg-amber-500/20 border border-amber-500/40"
                aria-label={`Categoria ${category}`}
              >
                {category}
              </span>
            </>
          )}
        </div>
        <div className="text-amber-200/90">{message}</div>
        {violationId && (
          <div className="mt-2 text-xs text-amber-300/70">
            <span className="sr-only">ID de violação: </span>
            <code className="font-mono">{violationId}</code>
          </div>
        )}
      </div>
    </div>
  )
}
