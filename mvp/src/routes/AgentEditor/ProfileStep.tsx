import { useEffect, useMemo, useRef, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
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

// Ordem canônica das seções no markdown. Espelha o que o `encodeInstructions`
// global produz no prompt final — assim "o que o user vê" no editor é
// byte-by-byte o que vai pro LLM (sem traduções implícitas).
const TEXT_FIELDS: ProfileTextField[] = ['role', 'goal', 'backstory']
const LIST_FIELDS: ProfileListField[] = ['rules', 'constraints']

// Template "Papel / Objetivo / Contexto" que aparece quando o user cria um
// agente novo (profile inteiramente vazio). Rules e constraints ficam fora do
// template inicial — são opcionais e o user pode adicionar `## Regras` no
// editor depois. O decoder reconhece esses headers se aparecerem.
const NEW_AGENT_TEMPLATE = [
  `## ${PROFILE_HEADERS.role}`,
  '',
  '',
  `## ${PROFILE_HEADERS.goal}`,
  '',
  '',
  `## ${PROFILE_HEADERS.backstory}`,
  '',
  '',
].join('\n')

// Mapas header→field e field→header (cobrem text + list). Mantemos local pra
// não acoplar com `instructionsCodec` (que cuida do prompt completo, incluindo
// tools/structured blocks que NÃO devem aparecer aqui).
const HEADER_TO_FIELD: Record<string, ProfileTextField | ProfileListField> = {}
const FIELD_IS_LIST = new Set<string>()
for (const field of [...TEXT_FIELDS, ...LIST_FIELDS]) {
  const header = PROFILE_HEADERS[field]
  HEADER_TO_FIELD[header] = field
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

// Produz o markdown que vai pro textarea a partir do profile. Sempre emite
// os 3 headers texto (Papel/Objetivo/Contexto) mesmo vazios pra que o user
// veja a estrutura. Rules e constraints só aparecem quando têm itens.
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

// Parser dual de listas: aceita `- item`, `* item`, `• item`. Vazias filtradas.
function parseListLines(body: string): string[] {
  return body
    .split('\n')
    .map((line) => line.replace(/^[\s]*[-•*][\s]+/, '').trim())
    .filter((line) => line.length > 0)
}

// Decoder inverso ao `profileToMarkdown`. Splita por linhas que começam com
// `## <header conhecido>` e atribui o body de cada seção ao campo certo.
// Headers desconhecidos são descartados — não corrompem o estado.
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
    // Texto livre sem headers: tudo vai pra Papel (mantém conteúdo sem
    // perder dados quando o user destrói a estrutura).
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

export function ProfileStep({ form, setForm, readonly }: ProfileStepProps) {
  // Estado local do texto do editor. Hidrata do form.profile no mount; depois
  // o fluxo é editor → form via debounce no onChange (evita re-renderizar o
  // textarea em cada keystroke por causa do round-trip encode→decode).
  const [editorText, setEditorText] = useState<string>(() => {
    if (isEmptyProfile(form.profile)) return NEW_AGENT_TEMPLATE
    return profileToMarkdown(form.profile)
  })

  // Sincroniza editor↔form quando o user carrega um agente existente
  // (form.profile chega populado depois do mount inicial). Só re-hidrata se
  // o texto ainda é o template — preserva edição em andamento.
  const lastHydratedRef = useRef<string>('')
  useEffect(() => {
    if (isEmptyProfile(form.profile)) return
    const encoded = profileToMarkdown(form.profile)
    if (encoded === lastHydratedRef.current) return
    if (editorText === NEW_AGENT_TEMPLATE || editorText === '') {
      lastHydratedRef.current = encoded
      setEditorText(encoded)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [form.profile])

  const setName = (name: string) => setForm((prev) => ({ ...prev, name }))

  const onEditorChange = (next: string) => {
    setEditorText(next)
    // Decode imediato — preview live precisa refletir a estrutura assim que
    // o user escreve `## Heading` ou `- item`. Sem debounce por enquanto;
    // se o parse virar gargalo num doc grande, fácil adicionar throttle.
    const parsed = markdownToProfile(next)
    setForm((prev) => ({ ...prev, profile: parsed }))
  }

  // Preview live — mesma stack que o ChatDeploymentSandbox e o ReviewStep
  // (react-markdown + remark-gfm). `editorText` é a fonte direta — não passa
  // pelo round-trip do form pra evitar inconsistência transitória durante a
  // digitação (ex: usuário digita `## ` e o cursor já mostraria como heading
  // antes do form ser atualizado).
  const previewSource = useMemo(() => editorText, [editorText])

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
        <div className="mb-3 flex items-baseline justify-between gap-3">
          <div>
            <h2 className="text-base font-bold text-fg">Perfil em Markdown</h2>
            <p className="mt-1 text-xs text-fg-muted">
              Edite à esquerda — pré-visualização à direita atualiza em tempo real. Use{' '}
              <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## Papel</code>,
              {' '}
              <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## Objetivo</code>,
              {' '}
              <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## Contexto</code>
              {' '}pros blocos canônicos. <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## Regras</code> e{' '}
              <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">## Restrições</code> aceitam listas com{' '}
              <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">- item</code>.
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <MarkdownEditor
            value={editorText}
            onChange={onEditorChange}
            disabled={readonly}
          />
          <MarkdownPreview source={previewSource} />
        </div>
      </div>
    </Card>
  )
}

interface MarkdownEditorProps {
  value: string
  onChange: (next: string) => void
  disabled?: boolean
}

function MarkdownEditor({ value, onChange, disabled }: MarkdownEditorProps) {
  return (
    <div className="flex min-h-[480px] flex-col rounded-lg border border-border bg-surface">
      <div className="flex items-center justify-between border-b border-border px-3 py-1.5 text-[10px] uppercase tracking-wider text-fg-dim">
        <span>Editor</span>
        <span className="font-mono">markdown</span>
      </div>
      <textarea
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        spellCheck
        className={cn(
          'flex-1 resize-none bg-transparent p-4 font-mono text-[13px] leading-relaxed text-fg',
          'placeholder:text-fg-dim focus:outline-none',
          'disabled:cursor-not-allowed disabled:opacity-60',
        )}
        placeholder={NEW_AGENT_TEMPLATE}
      />
    </div>
  )
}

function MarkdownPreview({ source }: { source: string }) {
  return (
    <div className="flex min-h-[480px] flex-col rounded-lg border border-border bg-bg-soft/40">
      <div className="flex items-center justify-between border-b border-border px-3 py-1.5 text-[10px] uppercase tracking-wider text-fg-dim">
        <span>Pré-visualização</span>
        <span className="font-mono">live</span>
      </div>
      <div className="flex-1 overflow-y-auto p-4">
        <div className="prose prose-sm dark:prose-invert max-w-none">
          <ReactMarkdown remarkPlugins={[remarkGfm]}>{source || ' '}</ReactMarkdown>
        </div>
      </div>
    </div>
  )
}
