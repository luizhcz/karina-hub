import { useEffect, useRef } from 'react'
import { BlockNoteSchema, defaultBlockSpecs } from '@blocknote/core'
import type { Block } from '@blocknote/core'
import { useCreateBlockNote } from '@blocknote/react'
import { BlockNoteView } from '@blocknote/mantine'
import '@blocknote/core/fonts/inter.css'
import '@blocknote/mantine/style.css'
import { useTheme } from '../../theme/ThemeProvider'
import { cn } from '../../ui'
import type { FormState } from './types'
import { useMarkdownPasteIntent } from './useMarkdownPasteIntent'

interface WorkerProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const SCOPE_MIN = 40
const SCOPE_MAX = 4000

// Template "Domínio de análise" pré-preenchido pra Worker novo. Estrutura
// guia: o que o Worker faz, dados que recebe, o que NÃO faz. Cada bloco
// vem com frase em itálico — user seleciona e escreve por cima.
const NEW_WORKER_TEMPLATE = [
  '## Escopo',
  '',
  '*Em 1-2 frases: que análise o Worker faz e em que domínio atua (ex: risco de crédito empresarial, classificação de fato relevante).*',
  '',
  '## Entradas esperadas',
  '',
  '*Quais dados chegam no input — campos, formato, granularidade. Ex: perfil do solicitante (porte, segmento, faturamento, histórico).*',
  '',
  '## Fora do escopo',
  '',
  '*O que o Worker NÃO faz — decisões finais, consultas externas em tempo real, recomendações de produto.*',
  '',
].join('\n')

const schema = BlockNoteSchema.create({ blockSpecs: defaultBlockSpecs })

export function WorkerProfileStep({ form, setForm, readonly }: WorkerProfileStepProps) {
  const { theme } = useTheme()
  const editor = useCreateBlockNote({ schema })
  const { hostRef, banner } = useMarkdownPasteIntent(editor, { readonly })

  // Mesmo padrão do ProfileStep (Custom): guard contra loop de
  // hidratação ↔ onChange. userEditedRef vira true só após edição real;
  // enquanto false, useEffect re-hidrata livremente (cobre mount + GET
  // tardio do edit mode).
  const userEditedRef = useRef(false)

  useEffect(() => {
    if (userEditedRef.current) return
    const md = form.workerScope.trim().length === 0
      ? NEW_WORKER_TEMPLATE
      : form.workerScope
    void (async () => {
      const blocks = await editor.tryParseMarkdownToBlocks(md)
      editor.replaceBlocks(editor.document, blocks as Block[])
    })()
  }, [editor, form.workerScope])

  const handleChange = async () => {
    const md = await editor.blocksToMarkdownLossy(editor.document)
    setForm((prev) => {
      if (prev.workerScope === md) return prev
      userEditedRef.current = true
      return { ...prev, workerScope: md.slice(0, SCOPE_MAX) }
    })
  }

  const trimmedLength = form.workerScope.trim().length
  const tooShort = trimmedLength > 0 && trimmedLength < SCOPE_MIN
  const empty = trimmedLength === 0

  return (
    <div className="space-y-5">
      <WorkerHelpCard />
      {banner}
      <div
        ref={hostRef}
        className={cn(
          'rounded-lg border border-border bg-surface py-6',
          'profile-blocknote-host',
        )}
      >
        <BlockNoteView
          editor={editor}
          editable={!readonly}
          theme={theme === 'dark' ? 'dark' : 'light'}
          onChange={handleChange}
        />
      </div>
      <div className="flex items-center justify-between px-1 text-[11px]">
        <span className="text-fg-muted">
          {empty
            ? 'Defina o domínio antes de salvar para o Worker funcionar bem em runtime.'
            : tooShort
              ? 'Domínio muito curto — descreva o escopo com mais detalhe pra orientar o LLM.'
              : 'Injetado como bloco anchor no system prompt; edits propagam pra próxima chamada.'}
        </span>
        <span className="text-fg-dim">
          {trimmedLength}/{SCOPE_MAX}
        </span>
      </div>
    </div>
  )
}

// Mini-guia voltado pra PM/PO criando Worker. Tom acessível, sem termos
// técnicos (workerScope, anchor, etc) — foco no PROPÓSITO.
function WorkerHelpCard() {
  return (
    <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
      <h3 className="text-[13px] font-semibold text-fg">Como descrever o domínio do Worker</h3>
      <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
        Worker é um especialista de domínio — recebe dados estruturados e produz uma
        análise. Descreva em texto comum: <strong className="text-fg">Escopo</strong>{' '}
        (que análise ele faz), <strong className="text-fg">Entradas esperadas</strong>{' '}
        (dados que chegam no input) e{' '}
        <strong className="text-fg">Fora do escopo</strong> (o que ele não faz).
        As frases <em className="text-fg">em itálico</em> no editor são só guias —
        selecione e escreva por cima.
      </p>
      <ul className="mt-3 space-y-1.5 text-[12px] text-fg-muted">
        <li>
          <span className="font-mono text-fg-dim">/</span> &nbsp;abre um menu com tipos de
          bloco (título, lista, citação, código, etc).
        </li>
        <li>
          <span className="font-mono text-fg-dim">##</span> + espaço &nbsp;cria um título de
          seção, como “Escopo”.
        </li>
        <li>
          <span className="font-mono text-fg-dim">-</span> + espaço &nbsp;começa uma lista
          com bolinhas.
        </li>
        <li>
          Selecione um trecho pra abrir um <strong className="text-fg">menu flutuante</strong>{' '}
          com negrito, itálico e link.
        </li>
      </ul>
    </div>
  )
}
