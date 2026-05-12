import { useEffect, useMemo, useRef } from 'react'
import { BlockNoteSchema, defaultBlockSpecs } from '@blocknote/core'
import type { Block } from '@blocknote/core'
import { useCreateBlockNote } from '@blocknote/react'
import { BlockNoteView } from '@blocknote/mantine'
import '@blocknote/core/fonts/inter.css'
import '@blocknote/mantine/style.css'
import { useTheme } from '../../theme/ThemeProvider'
import { Card, cn } from '../../ui'
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

// Template "Papel / Objetivo / Contexto" pré-preenchido quando o user cria
// um agente novo. Headings já formatados (## H2) — o BlockNote consome o
// markdown via tryParseMarkdownToBlocks no mount.
const NEW_AGENT_TEMPLATE = [
  `## ${PROFILE_HEADERS.role}`,
  '',
  '',
  `## ${PROFILE_HEADERS.goal}`,
  '',
  '',
  `## ${PROFILE_HEADERS.backstory}`,
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
  if (matches.length === 0) {
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

  // Markdown inicial: template default quando profile vazio (modo create),
  // ou encode do estado existente (modo edit). Calculado uma vez no mount —
  // mudanças subsequentes do form.profile são tratadas pelo useEffect de
  // hidratação abaixo.
  const initialMarkdown = useMemo(() => {
    if (isEmptyProfile(form.profile)) return NEW_AGENT_TEMPLATE
    return profileToMarkdown(form.profile)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const editor = useCreateBlockNote({ schema })

  // Hidratação assíncrona: tryParseMarkdownToBlocks é async e o BlockNote
  // cria o editor vazio por default. Carregamos o markdown inicial assim
  // que o editor está pronto.
  const hydratedRef = useRef(false)
  useEffect(() => {
    if (hydratedRef.current) return
    hydratedRef.current = true
    void (async () => {
      const blocks = await editor.tryParseMarkdownToBlocks(initialMarkdown)
      editor.replaceBlocks(editor.document, blocks as Block[])
    })()
  }, [editor, initialMarkdown])

  // Re-hidrata quando o form.profile chega populado depois do mount inicial
  // (caso clássico de edit mode: a página carrega o agente via GET, o setForm
  // injeta o profile real ~50-200ms depois do mount). Só dispara se o user
  // ainda não começou a editar (preserva edição em andamento).
  useEffect(() => {
    if (isEmptyProfile(form.profile)) return
    const encoded = profileToMarkdown(form.profile)
    if (encoded === initialMarkdown) return
    void (async () => {
      const blocks = await editor.tryParseMarkdownToBlocks(encoded)
      editor.replaceBlocks(editor.document, blocks as Block[])
    })()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.profile])

  const setName = (name: string) => setForm((prev) => ({ ...prev, name }))

  // Sync editor → form: a cada mudança, serializa blocks pra markdown (lossy)
  // e decodifica pros 5 campos do profile. lossy = preserva estrutura (heading,
  // listas, bold, code) mas não é byte-perfect — aceitável pra esse use case.
  const handleChange = async () => {
    const md = await editor.blocksToMarkdownLossy(editor.document)
    const parsed = markdownToProfile(md)
    setForm((prev) => ({ ...prev, profile: parsed }))
  }

  return (
    <Card padded={false} className="px-8 py-8 sm:px-10 sm:py-10">
      <div className="-mx-3 px-3 py-1.5">
        <input
          type="text"
          value={form.name}
          onChange={(e) => setName(e.target.value)}
          placeholder="Nome do agente"
          autoFocus
          disabled={readonly}
          className={cn(
            'w-full border-0 bg-transparent px-0 text-3xl font-bold tracking-tight text-fg sm:text-4xl',
            'placeholder:text-fg-dim focus:outline-none focus:ring-0',
            'disabled:cursor-not-allowed disabled:opacity-60',
          )}
        />
      </div>

      <div className="mt-8">
        <div className="mb-3">
          <h2 className="text-base font-bold text-fg">Perfil</h2>
          <p className="mt-1 text-xs text-fg-muted">
            Escreva em markdown — digite{' '}
            <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## </code> pra heading,
            {' '}
            <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">- </code> pra lista,
            {' '}
            <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">/ </code> abre menu de blocos.
          </p>
        </div>

        <div
          className={cn(
            'rounded-lg border border-border bg-surface',
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
    </Card>
  )
}
