import { useEffect, useState } from 'react'
import { Button, Textarea, cn } from '../../ui'
import type { AssistantOperacao } from './instructionsCodec'

interface AssistantDiffProps {
  /** Conteúdo da seção alvo no markdown atual (sem o header), ou string
   *  vazia quando a seção não existe. */
  current: string
  /** Texto sugerido pelo LLM — pode ser parágrafo, lista ou markdown solto. */
  suggested: string
  /** Verdadeiro quando a seção alvo já existe no markdown do user. Quando
   *  falso, "Substituir" fica disabled (vira efetivamente "criar nova"). */
  secaoExists: boolean
  /** Sugestão global (sem header alvo) — força modo "Mesclar global", toggle
   *  fica desabilitado. */
  secaoIsNull: boolean
  onApply: (op: AssistantOperacao, draft: string) => void
  onCancel: () => void
}

/**
 * Painel de revisão da sugestão com toggle "Substituir / Mesclar". A esquerda
 * mostra o conteúdo atual da seção (read-only) pra contexto; a direita, uma
 * textarea editável com o conteúdo a aplicar. O modo controla:
 *
 * - Substituir: troca o conteúdo da seção pela versão editada (ou cria a
 *   seção se ela não existe). Textarea começa com `suggested`.
 * - Mesclar: anexa o conteúdo ao fim da seção (preservando o existente).
 *   Textarea começa com `current + '\n\n' + suggested` pra que o user veja
 *   e edite o merge antes de aplicar.
 *
 * Sugestões globais (secao=null) só permitem Mesclar global — toggle fica
 * desabilitado.
 */
export function AssistantDiff({
  current,
  suggested,
  secaoExists,
  secaoIsNull,
  onApply,
  onCancel,
}: AssistantDiffProps) {
  // Default do modo:
  // - secaoIsNull → 'merge' (toggle disabled, append global).
  // - !secaoExists → 'merge' (Substituir disabled — vira criar seção nova).
  // - secaoExists → 'replace' (substituir é o caminho dominante).
  const initialMode: AssistantOperacao = secaoIsNull || !secaoExists ? 'mesclar' : 'substituir'
  const [mode, setMode] = useState<AssistantOperacao>(initialMode)

  const buildInitialDraft = (m: AssistantOperacao): string => {
    if (m === 'substituir') return suggested
    // Mesclar: pré-visualiza a concatenação. Quando não há current
    // (seção não existe ou está vazia) o draft fica só com a sugestão.
    const trimmedCurrent = current.trim()
    if (!trimmedCurrent) return suggested
    return `${trimmedCurrent}\n\n${suggested}`
  }

  const [draft, setDraft] = useState<string>(() => buildInitialDraft(initialMode))
  const [userEdited, setUserEdited] = useState(false)

  // Ao trocar de modo, recompõe o draft a partir do template novo — exceto
  // quando o user já editou o texto manualmente (preserva o trabalho dele).
  useEffect(() => {
    if (userEdited) return
    setDraft(buildInitialDraft(mode))
  }, [mode, userEdited])

  const replaceDisabled = !secaoExists || secaoIsNull
  const toggleDisabled = secaoIsNull

  return (
    <div className="rounded-lg border border-border bg-bg-soft p-3">
      <ModeToggle
        mode={mode}
        onChange={(next) => {
          setMode(next)
          // Trocar de modo reseta o draft (a menos que o user já tenha
          // editado). userEdited é mantido — se ele mexeu, mexer continua
          // valendo no novo modo.
        }}
        replaceDisabled={replaceDisabled}
        toggleDisabled={toggleDisabled}
        secaoIsNull={secaoIsNull}
        secaoExists={secaoExists}
      />

      <div className="mt-3 grid gap-2 sm:grid-cols-2">
        <div>
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-wider text-fg-dim">
            {secaoIsNull ? 'Documento atual' : secaoExists ? 'Atual (seção)' : 'Atual'}
          </p>
          <pre
            className={cn(
              'min-h-[88px] max-h-40 overflow-y-auto whitespace-pre-wrap rounded-md border border-border bg-surface px-2.5 py-2 font-sans text-xs text-fg-muted',
              !current.trim() && 'italic text-fg-dim',
            )}
          >
            {current.trim() || (secaoExists ? '(seção vazia)' : '(seção não existe — será criada)')}
          </pre>
        </div>
        <div>
          <p className="mb-1 flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-wider text-accent">
            {mode === 'substituir' ? 'Sugerido (substituir)' : 'Preview do merge'}
            {userEdited && <span className="text-fg-dim">· editado</span>}
          </p>
          <Textarea
            value={draft}
            onChange={(e) => {
              setDraft(e.target.value)
              setUserEdited(true)
            }}
            rows={6}
            className="text-xs"
            autoFocus
          />
        </div>
      </div>

      <div className="mt-3 flex items-center justify-end gap-2">
        <Button size="sm" variant="ghost" onClick={onCancel}>
          Cancelar
        </Button>
        <Button size="sm" onClick={() => onApply(mode, draft)} disabled={!draft.trim()}>
          Aplicar
        </Button>
      </div>
    </div>
  )
}

interface ModeToggleProps {
  mode: AssistantOperacao
  onChange: (next: AssistantOperacao) => void
  replaceDisabled: boolean
  toggleDisabled: boolean
  secaoIsNull: boolean
  secaoExists: boolean
}

function ModeToggle({
  mode,
  onChange,
  replaceDisabled,
  toggleDisabled,
  secaoIsNull,
  secaoExists,
}: ModeToggleProps) {
  // Hint de ajuda baseado no contexto da seção alvo. Texto curto, em PT-BR,
  // explica o trade-off pro PO/PM que talvez nunca tenha visto esse toggle.
  const hint = secaoIsNull
    ? 'Sugestão global — vai ser anexada como parágrafo no fim do perfil.'
    : !secaoExists
      ? 'Seção não existe no perfil atual — vai ser criada nova com este conteúdo.'
      : mode === 'substituir'
        ? 'Substituir troca todo o conteúdo da seção pela versão editada.'
        : 'Mesclar mantém o conteúdo atual e anexa o sugerido depois.'

  return (
    <div className="flex flex-wrap items-center gap-2">
      <div
        className={cn(
          'inline-flex rounded-md border border-border bg-surface p-0.5',
          toggleDisabled && 'opacity-60',
        )}
        role="radiogroup"
        aria-label="Como aplicar a sugestão"
      >
        <ToggleButton
          active={mode === 'substituir'}
          disabled={replaceDisabled}
          onClick={() => onChange('substituir')}
          label="Substituir"
          title={
            replaceDisabled
              ? secaoIsNull
                ? 'Sugestão global não substitui — só mescla no fim do perfil.'
                : 'Seção não encontrada — vai criar nova ao aplicar.'
              : 'Troca o conteúdo da seção pela versão editada.'
          }
        />
        <ToggleButton
          active={mode === 'mesclar'}
          disabled={false}
          onClick={() => onChange('mesclar')}
          label="Mesclar"
          title="Mantém o conteúdo da seção e anexa o sugerido."
        />
      </div>
      <span className="text-[11px] leading-snug text-fg-muted">{hint}</span>
    </div>
  )
}

interface ToggleButtonProps {
  active: boolean
  disabled: boolean
  onClick: () => void
  label: string
  title: string
}

function ToggleButton({ active, disabled, onClick, label, title }: ToggleButtonProps) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={active}
      onClick={onClick}
      disabled={disabled}
      title={title}
      className={cn(
        'rounded px-2.5 py-1 text-[11px] font-medium transition',
        active
          ? 'bg-accent text-accent-contrast shadow-sm'
          : 'text-fg-muted hover:bg-bg-soft hover:text-fg',
        disabled && 'cursor-not-allowed opacity-50 hover:bg-transparent hover:text-fg-muted',
      )}
    >
      {label}
    </button>
  )
}
