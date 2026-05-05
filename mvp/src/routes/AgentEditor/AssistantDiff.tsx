import { useState } from 'react'
import { Button, Textarea, cn } from '../../ui'

interface AssistantDiffProps {
  current: string
  suggested: string
  onApply: (value: string) => void
  onCancel: () => void
}

/**
 * Diff side-by-side compacto: à esquerda o valor atual (read-only), à direita
 * uma textarea pré-preenchida com a sugestão. O PO precisa revisar antes de
 * confirmar — fricção proposital pra preservar paternidade do conteúdo.
 */
export function AssistantDiff({ current, suggested, onApply, onCancel }: AssistantDiffProps) {
  const [draft, setDraft] = useState(suggested)
  const dirty = draft.trim() !== suggested.trim()

  return (
    <div className="rounded-lg border border-border bg-bg-soft p-3">
      <div className="grid gap-2 sm:grid-cols-2">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">
            Atual
          </p>
          <pre
            className={cn(
              'min-h-[88px] whitespace-pre-wrap rounded-md border border-border bg-surface px-2.5 py-2 font-sans text-xs text-fg-muted',
              !current.trim() && 'italic text-fg-dim',
            )}
          >
            {current.trim() || '(vazio)'}
          </pre>
        </div>
        <div>
          <p className="mb-1 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wider text-accent">
            Sugerido
            {dirty && <span className="text-fg-dim">· editado</span>}
          </p>
          <Textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            rows={4}
            className="text-xs"
            autoFocus
          />
        </div>
      </div>
      <div className="mt-3 flex items-center justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onCancel}>
          Cancelar
        </Button>
        <Button size="sm" onClick={() => onApply(draft)} disabled={!draft.trim()}>
          Aplicar
        </Button>
      </div>
    </div>
  )
}
