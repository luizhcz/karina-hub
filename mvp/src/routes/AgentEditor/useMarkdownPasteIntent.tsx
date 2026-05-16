import { useEffect, useRef, useState } from 'react'
import type { BlockNoteEditor } from '@blocknote/core'
import { Button, CloseIcon, cn } from '../../ui'
import { looksLikeMarkdown } from './detectMarkdown'

interface UseMarkdownPasteIntentOptions {
  /** Editor em modo somente-leitura não deve interceptar paste. */
  readonly?: boolean
}

interface UseMarkdownPasteIntentResult {
  /** Ref pro container que envolve o <BlockNoteView>. Escuta paste em capture. */
  hostRef: React.RefObject<HTMLDivElement | null>
  /** Banner inline (ou null) — render abaixo do help card. */
  banner: React.ReactNode
}

/**
 * Intercepta paste no editor BlockNote: se o texto cru parecer Markdown,
 * suspende o paste default e exibe um banner pra o user decidir entre
 * inserir como Markdown formatado ou como texto puro. Sem detecção → o
 * BlockNote trata o paste do jeito normal (não passa por aqui).
 *
 * Por que listener em capture no container e não no editor: o BlockNote
 * registra seu paste handler dentro do ProseMirror view; ouvir em capture
 * no DOM container deixa a gente ver o evento antes — e
 * <c>stopPropagation</c> impede o handler interno de também rodar.
 */
export function useMarkdownPasteIntent(
  editor: BlockNoteEditor<any, any, any>,
  options: UseMarkdownPasteIntentOptions = {},
): UseMarkdownPasteIntentResult {
  const { readonly = false } = options
  const [pending, setPending] = useState<string | null>(null)
  const hostRef = useRef<HTMLDivElement>(null)
  // Lê via ref no handler — evita re-attach do listener a cada toggle
  // de readonly. O handler sempre vê o valor atual sem stale closure.
  const readonlyRef = useRef(readonly)
  readonlyRef.current = readonly

  useEffect(() => {
    const node = hostRef.current
    if (!node) return

    const handler = (ev: Event) => {
      if (readonlyRef.current) return
      const clip = (ev as ClipboardEvent).clipboardData
      const text = clip?.getData('text/plain') ?? ''
      if (!text || !looksLikeMarkdown(text)) return
      ev.preventDefault()
      ev.stopPropagation()
      setPending(text)
    }

    node.addEventListener('paste', handler, true)
    return () => node.removeEventListener('paste', handler, true)
  }, [])

  const pasteAsMarkdown = () => {
    if (!pending) return
    editor.pasteMarkdown(pending)
    setPending(null)
  }

  const pasteAsPlain = () => {
    if (!pending) return
    editor.pasteText(pending)
    setPending(null)
  }

  const dismiss = () => setPending(null)

  const banner = pending ? (
    <PasteIntentBanner
      preview={pending}
      onMarkdown={pasteAsMarkdown}
      onPlain={pasteAsPlain}
      onDismiss={dismiss}
    />
  ) : null

  return { hostRef, banner }
}

interface PasteIntentBannerProps {
  preview: string
  onMarkdown: () => void
  onPlain: () => void
  onDismiss: () => void
}

function PasteIntentBanner({
  preview,
  onMarkdown,
  onPlain,
  onDismiss,
}: PasteIntentBannerProps) {
  const firstLine = preview.split('\n').find((l) => l.trim().length > 0) ?? ''
  const trimmed = firstLine.length > 60 ? firstLine.slice(0, 60) + '…' : firstLine
  const primaryRef = useRef<HTMLButtonElement>(null)

  // Banner é assertivo: aparece em reação a uma ação do user (paste) e
  // precisa puxar atenção sem virar diálogo modal. Foco no botão default
  // garante navegação por teclado (Enter confirma Markdown).
  useEffect(() => {
    primaryRef.current?.focus()
  }, [])

  return (
    <div
      role="region"
      aria-live="assertive"
      aria-label="Confirmar formato do texto colado"
      className={cn(
        'flex items-start gap-3 rounded-lg border border-accent/30 bg-accent/[0.06] p-3',
        'text-[12px]',
      )}
    >
      <MarkdownGlyph className="mt-0.5 h-5 w-5 shrink-0 text-accent" />
      <div className="min-w-0 flex-1">
        <p className="font-medium text-fg">
          Texto colado parece estar em Markdown
        </p>
        <p className="mt-0.5 truncate text-fg-muted">{trimmed}</p>
        <div className="mt-2 flex flex-wrap gap-2">
          <Button ref={primaryRef} size="sm" onClick={onMarkdown}>
            Colar como Markdown
          </Button>
          <Button size="sm" variant="ghost" onClick={onPlain}>
            Colar como texto puro
          </Button>
        </div>
      </div>
      <button
        type="button"
        onClick={onDismiss}
        aria-label="Descartar — você pode colar novamente com Ctrl+V"
        title="Descartar — você pode colar novamente com Ctrl+V"
        className="shrink-0 rounded p-1 text-fg-dim transition hover:bg-surface-hover hover:text-fg"
      >
        <CloseIcon className="h-4 w-4" />
      </button>
    </div>
  )
}

// Glifo "M↓" estilizado em SVG. Sem dependência nova — mantém o pacote
// de ícones do projeto enxuto. 24x24 viewBox seguindo o padrão do
// Icons.tsx do MVP.
function MarkdownGlyph({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.8}
      strokeLinecap="round"
      strokeLinejoin="round"
      className={className}
      aria-hidden
    >
      <rect x="3" y="5" width="18" height="14" rx="2" />
      <path d="M7 15V9l2.5 3L12 9v6" />
      <path d="M16 9v6" />
      <path d="M14 13l2 2 2-2" />
    </svg>
  )
}
