import { useEffect } from 'react'
import { cn } from './cn'
import { CloseIcon } from './Icons'
import { IconButton } from './IconButton'

interface ModalProps {
  open: boolean
  onClose: () => void
  title?: React.ReactNode
  description?: React.ReactNode
  children: React.ReactNode
  footer?: React.ReactNode
  /** controla largura máxima do modal */
  size?: 'sm' | 'md' | 'lg' | 'xl' | '2xl'
}

const sizeClasses: Record<NonNullable<ModalProps['size']>, string> = {
  sm: 'max-w-sm',
  md: 'max-w-md',
  lg: 'max-w-2xl',
  xl: 'max-w-5xl',
  '2xl': 'max-w-7xl',
}

export function Modal({ open, onClose, title, description, children, footer, size = 'md' }: ModalProps) {
  // Fecha com ESC + bloqueia scroll do body enquanto aberto.
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    const previousOverflow = document.body.style.overflow
    document.body.style.overflow = 'hidden'
    return () => {
      window.removeEventListener('keydown', onKey)
      document.body.style.overflow = previousOverflow
    }
  }, [open, onClose])

  if (!open) return null

  return (
    <div
      // Overlay token-aware: bg-fg/30 escurece em light (fg=navy) e
      // esbranquiça-suaviza em dark (fg=branco), criando contraste consistente
      // sem o "buraco preto" do bg-black/40 em fundos navy.
      className="fixed inset-0 z-50 flex items-center justify-center bg-fg/30 p-6 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      onClick={onClose}
    >
      <div
        className={cn(
          // Quick-win: rounded-2xl + shadow-xl + ring sutil pra finish bancário.
          // max-h limita a altura total; header e footer ficam fixos enquanto o
          // body rola — sem isso conteúdo grande (ex.: tela de aprovação) empurra
          // os botões pra fora da viewport.
          'flex max-h-[calc(100vh-3rem)] w-full flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-xl ring-1 ring-border/50',
          sizeClasses[size],
        )}
        onClick={(e) => e.stopPropagation()}
      >
        {(title || description) && (
          <div className="flex shrink-0 items-start justify-between gap-3 border-b border-border px-5 py-4">
            <div className="min-w-0">
              {title && <h2 className="text-base font-semibold text-fg">{title}</h2>}
              {description && <p className="mt-0.5 text-xs text-fg-muted">{description}</p>}
            </div>
            <IconButton aria-label="Fechar" onClick={onClose}>
              <CloseIcon className="h-4 w-4" />
            </IconButton>
          </div>
        )}
        <div className="flex-1 overflow-y-auto px-5 py-4">{children}</div>
        {footer && <div className="shrink-0 border-t border-border bg-bg-soft px-5 py-3">{footer}</div>}
      </div>
    </div>
  )
}
