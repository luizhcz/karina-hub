import { useState } from 'react'
import { Card, CardHeader, IconButton, Input, cn } from '../../ui'
import { ProfileStep } from './ProfileStep'
import type { FormState } from './types'

interface ConversationalProfileStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const UI_COMPONENT_MAX_LENGTH = 64

// Step do Conversational. Reusa o ProfileStep do Custom pra identificação e
// persona (mesmos campos: role, goal, backstory, rules, constraints — vão
// pro skeleton de instructions via encodeInstructions). Acrescenta um card
// "Componentes de UI" pra capturar os valores válidos do enum `ui_component`
// que o codec injeta no schema canônico { ui_component, message, output }.
export function ConversationalProfileStep({
  form,
  setForm,
  readonly,
}: ConversationalProfileStepProps) {
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
      <ProfileStep form={form} setForm={setForm} readonly={readonly} />

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
