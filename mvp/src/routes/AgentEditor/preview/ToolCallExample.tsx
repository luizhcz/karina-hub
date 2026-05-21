import { useMemo, useState } from 'react'
import { Badge } from '../../../ui'
import { synthesizeArgs } from './synthesizeArgs'
import type { EnrichedToolDescriptor } from './toolDescriptors'

interface Props {
  tools: EnrichedToolDescriptor[]
}

/**
 * Mostra um exemplo concreto do JSON `tool_call` que o modelo poderia emitir
 * pra uma das tools anexadas. Útil pra ancorar o mental model: o modelo
 * responde com `{name, arguments}` estruturado — não imprime no prompt.
 *
 * Só renderiza pra tools HTTP com schema parseável.
 */
export function ToolCallExample({ tools }: Props) {
  const samples = useMemo(
    () =>
      tools
        .filter((t): t is Extract<EnrichedToolDescriptor, { source: 'http' }> =>
          t.source === 'http' && t.inputSchema !== null && typeof t.inputSchema === 'object',
        )
        .map((t) => ({
          name: t.name,
          arguments: synthesizeArgs(t.inputSchema as object),
        })),
    [tools],
  )
  const [index, setIndex] = useState(0)
  if (samples.length === 0) {
    // Há tools anexadas mas nenhuma com schema parseável — mostra nota curta
    // pra explicar a ausência em vez de sumir silenciosamente.
    if (tools.length === 0) return null
    return (
      <section
        aria-label="Exemplo de tool_call"
        className="rounded-lg border border-border bg-fg-soft/5 px-4 py-3"
      >
        <header className="mb-1 flex items-center gap-2">
          <span className="text-base">{'>_'}</span>
          <h3 className="text-sm font-semibold text-fg">Como o modelo chamaria</h3>
        </header>
        <p className="text-[11px] text-fg-muted">
          Nenhuma ferramenta com schema declarado — defina input no formato JSON pra ver um exemplo aqui.
        </p>
      </section>
    )
  }

  const current = samples[index % samples.length]
  const pretty = JSON.stringify({ name: current.name, arguments: current.arguments }, null, 2)
  const showCycle = samples.length > 1

  return (
    <section
      aria-label="Exemplo de tool_call"
      className="rounded-lg border border-border bg-fg-soft/5 px-4 py-3"
    >
      <header className="mb-2 flex items-center gap-2">
        <span className="text-base">{'>_'}</span>
        <h3 className="text-sm font-semibold text-fg">Como o modelo chamaria</h3>
        <Badge tone="neutral">exemplo ilustrativo</Badge>
        {showCycle && (
          <button
            type="button"
            onClick={() => setIndex((i) => i + 1)}
            className="ml-auto rounded-md border border-border px-2 py-0.5 text-[11px] text-fg-muted transition hover:bg-surface-hover hover:text-fg"
          >
            próxima tool →
          </button>
        )}
      </header>
      <p className="mb-2 text-[11px] text-fg-muted">
        O modelo decide quando e como chamar. Argumentos sintetizados a partir do schema.
      </p>
      <pre className="m-0 max-h-72 overflow-auto rounded border border-border bg-bg-soft px-3 py-2 font-mono text-[11px] leading-snug text-fg">
        {pretty}
      </pre>
    </section>
  )
}
