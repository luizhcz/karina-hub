import { useEffect, useMemo, useState } from 'react'
import { Badge, CloseIcon, IconButton, cn } from '../../ui'
import { EVALUATOR_CATALOG, type EvaluatorKind } from './evaluatorMeta'

interface GlossaryDrawerProps {
  open: boolean
  onClose: () => void
  /** Quando informado, dá destaque visual aos evaluators usados na run aberta. */
  highlightedNames?: string[]
}

const KIND_TONE: Record<EvaluatorKind, 'neutral' | 'accent'> = {
  Local: 'neutral',
  Meai: 'accent',
}

const KIND_DESCRIPTION: Record<EvaluatorKind, string> = {
  Local:
    'Heurísticas determinísticas executadas em-processo, sem chamada de LLM. Custo zero, latência <5ms. Bom pra validações binárias (contém/não contém, chamou tool/não chamou).',
  Meai:
    'LLM-as-judge usando o modelo do projeto. Avaliação semântica — entende paráfrases, contradições, intenção. Custo ~$0.001-0.01 por chamada, latência 1-10s.',
}

export function GlossaryDrawer({ open, onClose, highlightedNames }: GlossaryDrawerProps) {
  const [filter, setFilter] = useState<'all' | EvaluatorKind>('all')
  const highlighted = useMemo(() => new Set(highlightedNames ?? []), [highlightedNames])

  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])

  if (!open) return null

  const filtered = filter === 'all'
    ? EVALUATOR_CATALOG
    : EVALUATOR_CATALOG.filter((e) => e.kind === filter)

  return (
    <div className="fixed inset-0 z-40 flex" role="dialog" aria-label="Glossário de evaluators">
      <div className="flex-1 bg-fg/20 backdrop-blur-[1px]" onClick={onClose} />
      <aside className="flex h-full w-[480px] max-w-[100vw] flex-col border-l border-border bg-surface shadow-2xl">
        <div className="flex shrink-0 items-start justify-between gap-3 border-b border-border px-4 py-3">
          <div>
            <p className="text-[10px] font-semibold uppercase tracking-widest text-fg-dim">Glossário</p>
            <h2 className="mt-0.5 text-base font-semibold text-fg">O que cada métrica avalia</h2>
            <p className="mt-1 text-xs text-fg-muted">
              Mostramos abaixo todos os evaluators que o auto-deploy usa nos presets Básica/Média/Avançada.
            </p>
          </div>
          <IconButton aria-label="Fechar" onClick={onClose}>
            <CloseIcon className="h-4 w-4" />
          </IconButton>
        </div>

        <div className="flex shrink-0 flex-wrap gap-1.5 border-b border-border px-4 py-2.5">
          {(['all', 'Local', 'Meai'] as const).map((k) => {
            const active = filter === k
            const label = k === 'all' ? 'Todos' : k === 'Local' ? 'Local (heurísticas)' : 'MEAI (LLM-as-judge)'
            const count = k === 'all' ? EVALUATOR_CATALOG.length : EVALUATOR_CATALOG.filter((e) => e.kind === k).length
            return (
              <button
                key={k}
                onClick={() => setFilter(k)}
                className={cn(
                  'rounded-full border px-2.5 py-1 text-[11px] font-medium transition',
                  active
                    ? 'border-accent bg-accent-subtle text-accent'
                    : 'border-border bg-surface text-fg-muted hover:bg-surface-hover hover:text-fg',
                )}
              >
                {label} <span className="text-fg-dim">{count}</span>
              </button>
            )
          })}
        </div>

        <div className="flex-1 overflow-y-auto">
          {(['Local', 'Meai'] as EvaluatorKind[]).filter((k) => filter === 'all' || filter === k).map((kind) => {
            const items = filtered.filter((e) => e.kind === kind)
            if (items.length === 0) return null
            return (
              <section key={kind} className="border-b border-border last:border-b-0">
                <header className="bg-bg-soft px-4 py-2.5">
                  <div className="flex items-center gap-2">
                    <Badge tone={KIND_TONE[kind]} className="text-[10px]">{kind}</Badge>
                    <span className="text-xs font-semibold text-fg">{kind === 'Local' ? 'Heurísticas' : 'LLM-as-judge'}</span>
                  </div>
                  <p className="mt-1 text-[11px] leading-relaxed text-fg-muted">{KIND_DESCRIPTION[kind]}</p>
                </header>
                <ul className="divide-y divide-border">
                  {items.map((e) => {
                    const isHighlighted = highlighted.has(e.name)
                    return (
                      <li
                        key={e.name}
                        className={cn(
                          'space-y-1.5 px-4 py-3',
                          isHighlighted && 'bg-accent-subtle/30',
                        )}
                      >
                        <div className="flex flex-wrap items-center gap-1.5">
                          <span className="text-sm font-semibold text-fg">{e.label}</span>
                          <span className="font-mono text-[10px] text-fg-dim">{e.name}</span>
                          {isHighlighted && (
                            <Badge tone="accent" className="text-[9px]">usado nesta run</Badge>
                          )}
                        </div>
                        <p className="text-xs leading-relaxed text-fg-muted">{e.fullDescription}</p>
                        <p className="text-[11px] text-fg-dim">
                          <span className="font-semibold text-fg-muted">Score:</span> {e.scoreMeaning}
                        </p>
                      </li>
                    )
                  })}
                </ul>
              </section>
            )
          })}
        </div>
      </aside>
    </div>
  )
}
