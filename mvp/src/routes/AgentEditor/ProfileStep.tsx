import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { Card, CloseIcon, IconButton, cn } from '../../ui'
import { PROFILE_HEADERS } from './instructionsCodec'
import { shortId } from './formCodec'
import type {
  FormState,
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

const TEXT_SUBTITLES: Record<ProfileTextField, string> = {
  role: 'Quem é o agente — a personagem ou função que ele ocupa em cada conversa.',
  goal: 'O resultado que ele precisa entregar e por que isso importa pro usuário.',
  backstory: 'Pano de fundo que o agente assume como verdade — empresa, produto, público-alvo.',
}

const LIST_SUBTITLES: Record<ProfileListField, string> = {
  rules: 'Comportamentos que o agente sempre deve seguir. Cada item vira um bullet no prompt.',
  constraints: 'O que o agente nunca pode fazer ou compartilhar. Cada item vira um bullet no prompt.',
}

const TEXT_PLACEHOLDER: Record<ProfileTextField, string> = {
  role: 'Você é um atendente especializado em pós-venda da Acme Corp.',
  goal: 'Resolver dúvidas sobre pedidos, prazos e trocas com empatia e clareza.',
  backstory: 'A Acme vende eletrônicos online. Clientes acessam pelo site/app.',
}

const LIST_ITEM_PLACEHOLDER: Record<ProfileListField, string> = {
  rules: 'Sempre confirme o pedido pelo número antes de orientar',
  constraints: 'Nunca compartilhar dados de outros clientes',
}

export function ProfileStep({ form, setForm, readonly }: ProfileStepProps) {
  const setName = (name: string) => setForm((prev) => ({ ...prev, name }))
  const setProfileText = (field: ProfileTextField, value: string) =>
    setForm((prev) => ({ ...prev, profile: { ...prev.profile, [field]: value } }))
  const setProfileList = (field: ProfileListField, value: string[]) =>
    setForm((prev) => ({ ...prev, profile: { ...prev.profile, [field]: value } }))

  return (
    <Card padded={false} className="px-8 py-8 sm:px-10 sm:py-10">
      <EditableBlock className="-mx-3 px-3 py-1.5">
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
      </EditableBlock>

      <div className="mt-10">
        {TEXT_FIELDS.map((field) => (
          <Section
            key={field}
            label={PROFILE_HEADERS[field]}
            subtitle={TEXT_SUBTITLES[field]}
          >
            <EditableBlock>
              <AutoTextarea
                value={form.profile[field]}
                onChange={(value) => setProfileText(field, value)}
                placeholder={TEXT_PLACEHOLDER[field]}
                disabled={readonly}
              />
            </EditableBlock>
          </Section>
        ))}

        {LIST_FIELDS.map((field) => (
          <Section
            key={field}
            label={PROFILE_HEADERS[field]}
            subtitle={LIST_SUBTITLES[field]}
          >
            <BulletListField
              values={form.profile[field]}
              onChange={(values) => setProfileList(field, values)}
              placeholder={LIST_ITEM_PLACEHOLDER[field]}
              disabled={readonly}
            />
          </Section>
        ))}
      </div>
    </Card>
  )
}

interface SectionProps {
  label: string
  subtitle: string
  children: React.ReactNode
}

function Section({ label, subtitle, children }: SectionProps) {
  return (
    <section className="border-t border-border pt-7 pb-7 last:pb-0">
      <div className="mb-3">
        <h2 className="text-base font-bold text-fg">{label}</h2>
        <p className="mt-1 text-xs text-fg-muted">{subtitle}</p>
      </div>
      {children}
    </section>
  )
}

interface EditableBlockProps {
  children: React.ReactNode
  className?: string
}

// Wrapper que adiciona affordance de "isso é editável": hover mostra fundo
// suave, focus-within reforça com ring accent. Margem negativa expande o bg
// pra fora do conteúdo, parecendo um bloco de doc estilo Notion.
function EditableBlock({ children, className }: EditableBlockProps) {
  return (
    <div
      className={cn(
        'rounded-md transition-colors -mx-2 px-2 py-1.5',
        'cursor-text hover:bg-bg-soft/70',
        'focus-within:bg-bg-soft focus-within:ring-1 focus-within:ring-accent/30',
        className,
      )}
    >
      {children}
    </div>
  )
}

interface AutoTextareaProps {
  value: string
  onChange: (value: string) => void
  placeholder: string
  disabled?: boolean
}

// Textarea que cresce com o conteúdo (sem scroll). Reseta height pra 'auto'
// antes de medir scrollHeight pra que diminua quando texto é apagado.
// useLayoutEffect evita flicker entre "old height + new content" e o ajuste.
function AutoTextarea({ value, onChange, placeholder, disabled }: AutoTextareaProps) {
  const ref = useRef<HTMLTextAreaElement>(null)

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    el.style.height = 'auto'
    el.style.height = `${el.scrollHeight}px`
  }, [value])

  // Recalcula em resize do viewport — line wrap muda altura quando largura muda.
  useEffect(() => {
    const el = ref.current
    if (!el) return
    const recompute = () => {
      el.style.height = 'auto'
      el.style.height = `${el.scrollHeight}px`
    }
    window.addEventListener('resize', recompute)
    return () => window.removeEventListener('resize', recompute)
  }, [])

  return (
    <textarea
      ref={ref}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      rows={1}
      className={cn(
        'block w-full resize-none overflow-hidden border-0 bg-transparent px-0 text-[15px] leading-relaxed text-fg',
        'placeholder:text-fg-dim focus:outline-none focus:ring-0',
        'disabled:cursor-not-allowed disabled:opacity-60',
      )}
    />
  )
}

interface BulletListFieldProps {
  values: string[]
  onChange: (values: string[]) => void
  placeholder: string
  disabled?: boolean
}

interface BulletRow {
  id: string
  value: string
}

// Lista inline de bullets editáveis. Cada item ganha o mesmo affordance de
// hover/focus do EditableBlock (bg soft + ring accent quando focado). Mantém
// ids estáveis pra que React não confunda linhas em remoção/adição.
function BulletListField({ values, onChange, placeholder, disabled }: BulletListFieldProps) {
  const [rows, setRows] = useState<BulletRow[]>(() =>
    values.length === 0
      ? [{ id: shortId(), value: '' }]
      : values.map((value) => ({ id: shortId(), value })),
  )

  const project = (next: BulletRow[]) =>
    next.map((r) => r.value.trim()).filter((v) => v.length > 0)

  const sync = (next: BulletRow[]) => {
    setRows(next)
    onChange(project(next))
  }

  const update = (id: string, value: string) => {
    sync(rows.map((r) => (r.id === id ? { ...r, value } : r)))
  }

  const remove = (id: string) => {
    const next = rows.filter((r) => r.id !== id)
    sync(next.length === 0 ? [{ id: shortId(), value: '' }] : next)
  }

  const add = () => {
    sync([...rows, { id: shortId(), value: '' }])
  }

  return (
    <ul>
      {rows.map((row) => (
        <li
          key={row.id}
          className={cn(
            'group flex items-center gap-3 rounded-md transition-colors -mx-2 px-2 py-1.5',
            'cursor-text hover:bg-bg-soft/70',
            'focus-within:bg-bg-soft focus-within:ring-1 focus-within:ring-accent/30',
          )}
        >
          <span aria-hidden="true" className="select-none text-base text-fg-dim">
            •
          </span>
          <input
            type="text"
            value={row.value}
            onChange={(e) => update(row.id, e.target.value)}
            placeholder={placeholder}
            disabled={disabled}
            className={cn(
              'flex-1 border-0 bg-transparent px-0 text-[15px] leading-relaxed text-fg',
              'placeholder:text-fg-dim focus:outline-none focus:ring-0',
              'disabled:cursor-not-allowed disabled:opacity-60',
            )}
          />
          <IconButton
            aria-label="Remover item"
            variant="ghost"
            size="sm"
            onClick={() => remove(row.id)}
            disabled={disabled}
            className="opacity-0 transition group-hover:opacity-100 group-focus-within:opacity-100"
          >
            <CloseIcon className="h-3.5 w-3.5" />
          </IconButton>
        </li>
      ))}
      <li className="pl-2 pt-2">
        <button
          type="button"
          onClick={add}
          disabled={disabled}
          className={cn(
            'flex items-center gap-2 text-xs font-medium text-fg-muted transition hover:text-accent',
            'disabled:cursor-not-allowed disabled:opacity-60',
          )}
        >
          <span aria-hidden="true" className="font-mono text-sm">
            +
          </span>
          Adicionar
        </button>
      </li>
    </ul>
  )
}
