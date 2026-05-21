import { useEffect, useRef, useState } from 'react'
import { BlockNoteSchema, defaultBlockSpecs } from '@blocknote/core'
import type { Block } from '@blocknote/core'
import { useCreateBlockNote } from '@blocknote/react'
import { BlockNoteView } from '@blocknote/mantine'
import '@blocknote/core/fonts/inter.css'
import '@blocknote/mantine/style.css'
import { useTheme } from '../../theme/ThemeProvider'
import { HelpIcon, Modal, cn } from '../../ui'
import type { FormState } from './types'
import { useMarkdownPasteIntent } from './useMarkdownPasteIntent'

interface ProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

// Template pré-preenchido pra agente novo. Cada heading vem com uma frase-guia
// em itálico — o user lê, seleciona o trecho em itálico e escreve por cima
// (mais rápido que partir de página em branco). Regras/Restrições incluem um
// bullet de exemplo pra mostrar o formato esperado. As seções são apenas
// sugestões — o usuário pode reorganizar, renomear ou criar novas livremente.
const NEW_AGENT_TEMPLATE = [
  '## Papel',
  '',
  '*Em 1-2 frases: quem é o agente e que personagem ele assume na conversa.*',
  '',
  '## Objetivo',
  '',
  '*O resultado concreto que ele precisa entregar e por que isso importa pro usuário.*',
  '',
  '## Contexto',
  '',
  '*Pano de fundo que ele assume como verdade — empresa, produto, público-alvo, dados disponíveis.*',
  '',
  '## Regras de atuação',
  '',
  '- *Um comportamento que o agente sempre deve seguir (ex: confirmar dados antes de agir).*',
  '',
  '## Restrições',
  '',
  '- *O que o agente nunca pode fazer ou compartilhar (ex: não dar conselho jurídico).*',
  '',
].join('\n')

// Schema do BlockNote. defaultBlockSpecs cobre os blocos que o nosso markdown
// usa hoje (heading, bullet/numbered list, paragraph, code block, quote).
// Mantemos só esse subset — block customizado fica pra outra entrega.
const schema = BlockNoteSchema.create({ blockSpecs: defaultBlockSpecs })

export function ProfileStep({ form, setForm, readonly }: ProfileStepProps) {
  const { theme } = useTheme()
  const [helpOpen, setHelpOpen] = useState(false)

  const editor = useCreateBlockNote({ schema })
  const { hostRef, banner } = useMarkdownPasteIntent(editor, { readonly })

  // Quebra-loop de hidratação ↔ onChange:
  // - userEditedRef: vira true no primeiro edit REAL do user (i.e. onChange
  //   onde o markdown mudou de fato). Enquanto false, o useEffect re-hidrata
  //   livremente — cobre mount inicial (CREATE com template) E chegada tardia
  //   do GET em edit mode (profile vazio → populado depois do mount).
  // - Guard no setForm: compara o markdown atual do form com o produzido pelo
  //   onChange pra absorver o eco do replaceBlocks. Sem isso, o ciclo é:
  //   replaceBlocks → onChange → setForm → useEffect → replaceBlocks…
  //   (Maximum update depth, React error #185).
  const userEditedRef = useRef(false)

  useEffect(() => {
    if (userEditedRef.current) return
    const md = form.profile.trim().length === 0 ? NEW_AGENT_TEMPLATE : form.profile
    void (async () => {
      const blocks = await editor.tryParseMarkdownToBlocks(md)
      editor.replaceBlocks(editor.document, blocks as Block[])
    })()
  }, [editor, form.profile])

  const handleChange = async () => {
    const md = await editor.blocksToMarkdownLossy(editor.document)
    setForm((prev) => {
      if (prev.profile === md) return prev
      userEditedRef.current = true
      return { ...prev, profile: md }
    })
  }

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-end">
        <button
          type="button"
          onClick={() => setHelpOpen(true)}
          className="inline-flex items-center gap-1.5 rounded-md px-2 py-1 text-[12px] text-fg-muted transition hover:bg-bg-soft hover:text-fg"
          title="Como descrever seu agente"
        >
          <HelpIcon className="h-4 w-4" />
          Como descrever seu agente
        </button>
      </div>
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

      <Modal
        open={helpOpen}
        onClose={() => setHelpOpen(false)}
        title="Como descrever seu agente"
        size="md"
      >
        <div className="space-y-3 text-sm">
          <p className="text-[12px] leading-relaxed text-fg-muted">
            Escreva como se estivesse explicando pro agente o trabalho dele — em texto comum.
            Recomendamos cobrir três pontos: <strong className="text-fg">Papel</strong> (quem é),
            {' '}
            <strong className="text-fg">Objetivo</strong> (o que deve entregar) e
            {' '}
            <strong className="text-fg">Contexto</strong> (pano de fundo do produto/cliente).
            {' '}
            As frases <em className="text-fg">em itálico</em> no editor são só guias —
            selecione e escreva por cima. Você pode renomear seções e criar novas conforme precisar.
          </p>
          <ul className="space-y-1.5 text-[12px] text-fg-muted">
            <li>
              <span className="font-mono text-fg-dim">/</span> &nbsp;abre um menu com tipos de
              bloco (título, lista, citação, código, etc).
            </li>
            <li>
              <span className="font-mono text-fg-dim">##</span> + espaço &nbsp;cria um título de
              seção, como “Papel”.
            </li>
            <li>
              <span className="font-mono text-fg-dim">-</span> + espaço &nbsp;começa uma lista
              com bolinhas. Enter pula pro próximo item; Enter duas vezes encerra a lista.
            </li>
            <li>
              Selecione um trecho pra abrir um <strong className="text-fg">menu flutuante</strong>{' '}
              com negrito, itálico e link.
            </li>
          </ul>
        </div>
      </Modal>
    </div>
  )
}
