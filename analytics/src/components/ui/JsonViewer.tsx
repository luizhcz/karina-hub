/**
 * JsonViewer — exibe payload textual (JSON, JSON-em-fence ou texto puro)
 * de forma legível, com botão de copy.
 *
 * Reusabilidade: domain-agnostic. Usado pelo drawer de Execuções (input/output/
 * node.output) e pretendido pra LLM Calls forensic futuro. Não conhece o
 * shape — apenas tenta `JSON.parse` e fall-back pra texto.
 *
 * Decisões:
 *   - Detecta ```json fence comum em LLM responses e tira antes de parsear.
 *   - Pretty-print com 2 espaços (legível, não consome largura demais).
 *   - Copy escreve o texto ORIGINAL (preserva fence quando havia) — analista
 *     pode colar de volta no playground sem perda.
 *   - Limit de altura via maxHeightClass (default h-64); conteúdo maior
 *     scrolla. Pra altura completa o caller passa `maxHeightClass="h-auto"`.
 *
 * NÃO faz: syntax highlight (overkill V1), tree expand/collapse interativo,
 * diff entre 2 payloads (próximo do LLM Calls).
 *
 * Exemplo: <JsonViewer value={execution.output} ariaLabel="output da execução" />
 */
import { useState } from 'react'
import { cn } from './cn'

interface JsonViewerProps {
  value: string | null | undefined
  ariaLabel?: string
  maxHeightClass?: string
  /** Quando true, mostra um caption pequeno acima ("X caracteres", "JSON detectado"). */
  showMeta?: boolean
  className?: string
}

const FENCE_RE = /^```(?:json)?\s*\n([\s\S]*?)\n```$/i

function tryFormat(raw: string): { display: string; isJson: boolean } {
  const trimmed = raw.trim()
  if (trimmed.length === 0) return { display: '', isJson: false }

  // Strip de markdown fence ```json ... ``` que vários providers (OpenAI,
  // Anthropic) usam ao serializar tool/JSON outputs.
  const fenceMatch = trimmed.match(FENCE_RE)
  const candidate = fenceMatch ? fenceMatch[1].trim() : trimmed

  // Tenta parse só quando o primeiro char é { ou [ pra evitar custo em
  // strings que claramente não são JSON. JSON.parse é safe (não eval).
  if (candidate.startsWith('{') || candidate.startsWith('[')) {
    try {
      const parsed = JSON.parse(candidate)
      return { display: JSON.stringify(parsed, null, 2), isJson: true }
    } catch {
      // cai pra texto cru
    }
  }
  return { display: raw, isJson: false }
}

export function JsonViewer({
  value,
  ariaLabel,
  maxHeightClass = 'max-h-64',
  showMeta = false,
  className,
}: JsonViewerProps) {
  const [copied, setCopied] = useState(false)

  if (value == null || value === '') {
    return (
      <div className="rounded-md border border-dashed border-border px-3 py-4 text-center text-xs text-fg-dim">
        Vazio
      </div>
    )
  }

  const { display, isJson } = tryFormat(value)

  function handleCopy() {
    if (typeof navigator === 'undefined' || !navigator.clipboard) return
    void navigator.clipboard.writeText(value as string).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }

  return (
    <div className={cn('flex flex-col gap-1', className)}>
      <div className="flex items-center justify-between gap-2">
        {showMeta ? (
          <span className="text-[11px] text-fg-dim">
            {isJson ? 'JSON' : 'Texto'} · {value.length.toLocaleString('pt-BR')} char
          </span>
        ) : (
          <span />
        )}
        <button
          type="button"
          onClick={handleCopy}
          className="rounded-md px-2 py-0.5 text-[11px] text-fg-muted transition hover:bg-surface-hover hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30"
          aria-label={`Copiar ${ariaLabel ?? 'conteúdo'}`}
        >
          {copied ? '✓ copiado' : 'copiar'}
        </button>
      </div>
      <pre
        className={cn(
          'overflow-auto rounded-md border border-border bg-surface-hover p-3 font-mono text-[11px] leading-relaxed text-fg whitespace-pre-wrap break-words',
          maxHeightClass,
        )}
        aria-label={ariaLabel}
      >
        {display}
      </pre>
    </div>
  )
}
