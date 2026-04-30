import { useToastStore, type ToastKind } from '../../stores/toast'

const KIND_STYLES: Record<ToastKind, string> = {
  success: 'bg-green-500/10 border-green-500/40 text-green-200',
  error: 'bg-red-500/10 border-red-500/40 text-red-200',
  info: 'bg-blue-500/10 border-blue-500/40 text-blue-200',
}

export function Toaster() {
  const toasts = useToastStore((s) => s.toasts)
  const dismiss = useToastStore((s) => s.dismiss)

  if (toasts.length === 0) return null

  return (
    <div className="fixed top-4 right-4 z-50 flex flex-col gap-2 pointer-events-none">
      {toasts.map((t) => (
        <div
          key={t.id}
          role="status"
          className={`pointer-events-auto rounded-lg border px-4 py-3 shadow-lg backdrop-blur-sm max-w-sm ${KIND_STYLES[t.kind]}`}
        >
          <div className="flex items-start gap-2">
            <p className="text-sm flex-1 whitespace-pre-wrap">{t.message}</p>
            <button
              type="button"
              onClick={() => dismiss(t.id)}
              className="text-text-dimmed hover:text-text-primary text-xs flex-shrink-0"
              aria-label="Fechar"
            >
              ✕
            </button>
          </div>
        </div>
      ))}
    </div>
  )
}
