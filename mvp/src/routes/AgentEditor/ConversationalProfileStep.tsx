import { useState } from 'react'
import { Card, CardHeader, IconButton, Input, cn } from '../../ui'
import type { FormState } from './types'

interface ConversationalProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const PERSONA_MIN = 30
const PERSONA_MAX = 4000
const UI_COMPONENT_MAX_LENGTH = 64

// Step próprio do Conversational. Captura nome + persona + lista de
// ui_components que o agente pode emitir. Persona vai pras instructions
// skeleton via metadata['x-conversational-persona']; ui_components viram
// enum no schema canônico { ui_component, message, output } injetado pelo
// codec no save. Frontend chat consome o enum pra dirigir o renderer.
export function ConversationalProfileStep({
  form,
  setForm,
  readonly,
}: ConversationalProfileStepProps) {
  const personaTrimmedLength = form.conversationalPersona.trim().length
  const personaTooShort = personaTrimmedLength > 0 && personaTrimmedLength < PERSONA_MIN
  const personaEmpty = personaTrimmedLength === 0

  const setName = (value: string) =>
    setForm((prev) => ({ ...prev, name: value }))

  const setPersona = (value: string) =>
    setForm((prev) => ({
      ...prev,
      conversationalPersona: value.slice(0, PERSONA_MAX),
    }))

  const addUiComponent = (value: string) => {
    const trimmed = value.trim().slice(0, UI_COMPONENT_MAX_LENGTH)
    if (!trimmed) return
    setForm((prev) =>
      prev.conversationalUiComponents.includes(trimmed)
        ? prev
        : { ...prev, conversationalUiComponents: [...prev.conversationalUiComponents, trimmed] },
    )
  }

  const removeUiComponent = (value: string) =>
    setForm((prev) => ({
      ...prev,
      conversationalUiComponents: prev.conversationalUiComponents.filter((c) => c !== value),
    }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Identificação"
          description="Nome do agente como aparece na listagem e no chat."
        />
        <div>
          <label
            htmlFor="conversational-name"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Nome do Conversational
          </label>
          <Input
            id="conversational-name"
            value={form.name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Ex.: Assistente de Boleta (Assessor)"
            disabled={readonly}
            className="mt-1"
          />
        </div>
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Persona"
          description="Texto livre PT-BR descrevendo papel, personalidade, estilo de fala e restrições. O codec concatena na seção 'Persona' do system prompt."
        />
        <div>
          <label
            htmlFor="conversational-persona"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Descrição da persona
          </label>
          <textarea
            id="conversational-persona"
            value={form.conversationalPersona}
            onChange={(e) => setPersona(e.target.value)}
            placeholder="Ex.: Você é um assistente de boleta para assessores. Tom profissional, sem informalidade. Sempre confirme a conta do cliente antes de qualquer operação. Recuse pedidos fora do escopo de operações financeiras."
            disabled={readonly}
            aria-describedby="conversational-persona-help"
            className="mt-1 block min-h-[180px] w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 text-sm leading-relaxed text-fg shadow-sm focus:border-accent focus:outline-none focus:ring-2 focus:ring-accent/30 disabled:cursor-not-allowed disabled:opacity-60"
          />
          <div
            id="conversational-persona-help"
            className="mt-1.5 flex items-center justify-between text-[11px]"
          >
            <span className="text-fg-muted">
              {personaEmpty
                ? 'Persona vazia — defina pra dirigir o tom e o escopo do agente.'
                : personaTooShort
                  ? 'Persona curta. Inclua papel, tom e restrições pra orientar bem o LLM.'
                  : 'Será injetada no system prompt; edits propagam pra próxima conversa.'}
            </span>
            <span className="text-fg-dim">
              {personaTrimmedLength}/{PERSONA_MAX}
            </span>
          </div>
        </div>
      </Card>

      <Card className="space-y-3">
        <CardHeader
          title="Componentes de UI"
          description="Identificadores que o agente pode emitir no campo `ui_component`. O codec injeta como enum no schema fixo { ui_component, message, output }; o frontend chat usa pra dirigir o renderer (ou cair pro fallback genérico)."
        />
        <UiComponentEditor
          values={form.conversationalUiComponents}
          onAdd={addUiComponent}
          onRemove={removeUiComponent}
          readonly={readonly}
        />
      </Card>
    </div>
  )
}

interface UiComponentEditorProps {
  values: string[]
  onAdd: (value: string) => void
  onRemove: (value: string) => void
  readonly: boolean
}

function UiComponentEditor({ values, onAdd, onRemove, readonly }: UiComponentEditorProps) {
  const [draft, setDraft] = useState('')

  const commit = () => {
    if (!draft.trim()) return
    onAdd(draft)
    setDraft('')
  }

  return (
    <div className="space-y-2">
      <div className="flex flex-wrap gap-2">
        {values.length === 0 ? (
          <p className="text-xs italic text-fg-dim">
            Nenhum componente declarado. Adicione ao menos um (ex.: <code>text</code>, <code>card</code>, <code>list</code>).
          </p>
        ) : (
          values.map((value) => (
            <span
              key={value}
              className={cn(
                'group inline-flex items-center gap-1 rounded-full border border-accent/30 bg-accent-subtle px-2.5 py-1 text-xs font-medium text-accent',
              )}
            >
              <code className="font-mono text-[11px]">{value}</code>
              {!readonly && (
                <button
                  type="button"
                  onClick={() => onRemove(value)}
                  aria-label={`Remover ${value}`}
                  className="ml-0.5 rounded-full p-0.5 text-accent/70 transition hover:bg-accent/10 hover:text-accent"
                >
                  ×
                </button>
              )}
            </span>
          ))
        )}
      </div>
      <div className="flex gap-2">
        <Input
          value={draft}
          onChange={(e) => setDraft(e.target.value.slice(0, UI_COMPONENT_MAX_LENGTH))}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              commit()
            }
          }}
          placeholder="Novo identificador (ex.: card)"
          disabled={readonly}
          className="flex-1"
        />
        <IconButton
          type="button"
          variant="ghost"
          onClick={commit}
          disabled={readonly || !draft.trim()}
          aria-label="Adicionar componente"
        >
          +
        </IconButton>
      </div>
      <p className="text-[11px] text-fg-dim">
        Identificadores em snake_case ou kebab-case (ex.: <code>order_card</code>, <code>incomplete-form</code>).
      </p>
    </div>
  )
}
