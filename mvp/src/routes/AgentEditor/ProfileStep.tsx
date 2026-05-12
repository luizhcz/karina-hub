import { useEffect, useRef } from 'react'
import { BlockNoteSchema, defaultBlockSpecs } from '@blocknote/core'
import type { Block } from '@blocknote/core'
import { useCreateBlockNote } from '@blocknote/react'
import { BlockNoteView } from '@blocknote/mantine'
import '@blocknote/core/fonts/inter.css'
import '@blocknote/mantine/style.css'
import { useTheme } from '../../theme/ThemeProvider'
import { cn } from '../../ui'
import { PROFILE_HEADERS } from './instructionsCodec'
import type {
  FormState,
  ProfileFields,
  ProfileListField,
  ProfileTextField,
} from './types'

interface ProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const TEXT_FIELDS: ProfileTextField[] = ['role', 'goal', 'backstory']
const LIST_FIELDS: ProfileListField[] = ['rules', 'constraints']

// Template pré-preenchido pra agente novo. Cada heading vem com uma frase-guia
// em itálico — o user lê, seleciona o trecho em itálico e escreve por cima
// (mais rápido que partir de página em branco). Regras/Restrições incluem um
// bullet de exemplo pra mostrar o formato esperado.
const NEW_AGENT_TEMPLATE = [
  `## ${PROFILE_HEADERS.role}`,
  '',
  '*Em 1-2 frases: quem é o agente e que personagem ele assume na conversa.*',
  '',
  `## ${PROFILE_HEADERS.goal}`,
  '',
  '*O resultado concreto que ele precisa entregar e por que isso importa pro usuário.*',
  '',
  `## ${PROFILE_HEADERS.backstory}`,
  '',
  '*Pano de fundo que ele assume como verdade — empresa, produto, público-alvo, dados disponíveis.*',
  '',
  `## ${PROFILE_HEADERS.rules}`,
  '',
  '- *Um comportamento que o agente sempre deve seguir (ex: confirmar dados antes de agir).*',
  '',
  `## ${PROFILE_HEADERS.constraints}`,
  '',
  '- *O que o agente nunca pode fazer ou compartilhar (ex: não dar conselho jurídico).*',
  '',
].join('\n')

const HEADER_TO_FIELD: Record<string, ProfileTextField | ProfileListField> = {}
const FIELD_IS_LIST = new Set<string>()
for (const field of [...TEXT_FIELDS, ...LIST_FIELDS]) {
  HEADER_TO_FIELD[PROFILE_HEADERS[field]] = field
}
for (const field of LIST_FIELDS) FIELD_IS_LIST.add(field)

function isEmptyProfile(p: ProfileFields): boolean {
  return (
    p.role.trim() === ''
    && p.goal.trim() === ''
    && p.backstory.trim() === ''
    && p.rules.length === 0
    && p.constraints.length === 0
  )
}

function profileToMarkdown(p: ProfileFields): string {
  // Caso especial: agentes com instructions livres (sem headers canônicos)
  // caem todo o conteúdo em `role` via decodeInstructions fallback. Se role
  // já contém markdown estruturado (qualquer `## Header`), emite direto sem
  // wrap em `## Papel` — preserva a estrutura original do user e evita
  // duplicar headers no editor (`## Papel\n## Shared State`).
  const onlyRolePopulated =
    p.role.trim().length > 0
    && p.goal.trim().length === 0
    && p.backstory.trim().length === 0
    && p.rules.length === 0
    && p.constraints.length === 0
  if (onlyRolePopulated && /^##\s+/m.test(p.role)) {
    return p.role
  }

  const blocks: string[] = []
  for (const field of TEXT_FIELDS) {
    blocks.push(`## ${PROFILE_HEADERS[field]}`)
    blocks.push('')
    if (p[field]) blocks.push(p[field])
    blocks.push('')
  }
  for (const field of LIST_FIELDS) {
    const items = p[field]
    if (items.length === 0) continue
    blocks.push(`## ${PROFILE_HEADERS[field]}`)
    blocks.push('')
    for (const item of items) blocks.push(`- ${item}`)
    blocks.push('')
  }
  return blocks.join('\n').replace(/\n{3,}/g, '\n\n')
}

function parseListLines(body: string): string[] {
  return body
    .split('\n')
    .map((line) => line.replace(/^[\s]*[-•*][\s]+/, '').trim())
    .filter((line) => line.length > 0)
}

function markdownToProfile(text: string): ProfileFields {
  const next: ProfileFields = {
    role: '',
    goal: '',
    backstory: '',
    rules: [],
    constraints: [],
  }
  const normalized = text.replace(/\r\n/g, '\n')
  const matches = Array.from(normalized.matchAll(/^##\s+(.+?)\s*$/gm))

  // Fallback "tudo em role" cobre dois casos:
  // 1. Sem header algum (texto livre).
  // 2. Headers presentes mas NENHUM é canônico — usuário tem markdown
  //    custom (## Shared State, ## Tom, etc). Sem isso, o iter descartaria
  //    tudo e devolveria profile vazio.
  const hasKnownHeader = matches.some((m) => HEADER_TO_FIELD[m[1].trim()])
  if (matches.length === 0 || !hasKnownHeader) {
    next.role = normalized.trim()
    return next
  }

  for (let i = 0; i < matches.length; i++) {
    const m = matches[i]
    const header = m[1].trim()
    const field = HEADER_TO_FIELD[header]
    const start = (m.index ?? 0) + m[0].length
    const end = i + 1 < matches.length ? (matches[i + 1].index ?? normalized.length) : normalized.length
    const body = normalized.slice(start, end).trim()
    if (!field) continue
    if (FIELD_IS_LIST.has(field)) {
      next[field as ProfileListField] = parseListLines(body)
    } else {
      next[field as ProfileTextField] = body
    }
  }
  return next
}

// Schema do BlockNote. defaultBlockSpecs cobre os blocos que o nosso markdown
// usa hoje (heading, bullet/numbered list, paragraph, code block, quote).
// Mantemos só esse subset — block customizado fica pra outra entrega.
const schema = BlockNoteSchema.create({ blockSpecs: defaultBlockSpecs })

export function ProfileStep({ form, setForm, readonly }: ProfileStepProps) {
  const { theme } = useTheme()

  const editor = useCreateBlockNote({ schema })

  // Quebra-loop de hidratação ↔ onChange:
  // - userEditedRef: vira true no primeiro edit REAL do user (i.e. onChange
  //   onde o markdown encodado mudou). Enquanto false, o useEffect re-hidrata
  //   livremente — cobre mount inicial (CREATE com template) E chegada tardia
  //   do GET em edit mode (profile vazio → populado depois do mount).
  // - Guard no setForm: comparamos profileToMarkdown(parsed) vs profileToMarkdown(prev)
  //   pra absorver o eco do onChange disparado por replaceBlocks. Sem isso,
  //   o ciclo é: replaceBlocks → onChange → setForm → useEffect → replaceBlocks…
  //   (Maximum update depth, React error #185).
  const userEditedRef = useRef(false)

  useEffect(() => {
    if (userEditedRef.current) return
    const md = isEmptyProfile(form.profile)
      ? NEW_AGENT_TEMPLATE
      : profileToMarkdown(form.profile)
    void (async () => {
      const blocks = await editor.tryParseMarkdownToBlocks(md)
      editor.replaceBlocks(editor.document, blocks as Block[])
    })()
  }, [editor, form.profile])

  const handleChange = async () => {
    const md = await editor.blocksToMarkdownLossy(editor.document)
    const parsed = markdownToProfile(md)
    setForm((prev) => {
      // Comparação por markdown encodado: estrutura idêntica → sem mudança.
      // Ignora variações de whitespace/ordem que profileToMarkdown normaliza.
      if (profileToMarkdown(prev.profile) === profileToMarkdown(parsed)) {
        return prev
      }
      userEditedRef.current = true
      return { ...prev, profile: parsed }
    })
  }

  return (
    <div className="space-y-5">
      <EditorHelpCard />
      <div
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
    </div>
  )
}

// Mini-guia voltado pra PM/PO que nunca usou Notion/markdown. Explica os
// gestos que o BlockNote aceita em linguagem de produto (sem "headings",
// "blocos", "WYSIWYG") — foco no que o user CONSEGUE FAZER. Mantido curto
// pra não competir com o editor logo abaixo.
function EditorHelpCard() {
  return (
    <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
      <h3 className="text-[13px] font-semibold text-fg">Como descrever seu agente</h3>
      <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
        Escreva como se estivesse explicando pro agente o trabalho dele — em texto comum.
        Recomendamos cobrir três pontos: <strong className="text-fg">Papel</strong> (quem é),
        {' '}
        <strong className="text-fg">Objetivo</strong> (o que deve entregar) e
        {' '}
        <strong className="text-fg">Contexto</strong> (pano de fundo do produto/cliente).
        {' '}
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
  )
}
