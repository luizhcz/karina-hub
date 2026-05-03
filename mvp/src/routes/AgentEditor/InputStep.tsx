import { Card, CardHeader, JsonSchemaBuilder, Textarea, cn } from '../../ui'
import type { FormState, StructuredSection } from './types'

interface InputStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

interface ModeCardProps {
  active: boolean
  title: string
  description: string
  onClick: () => void
  disabled: boolean
}

function ModeCard({ active, title, description, onClick, disabled }: ModeCardProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className={cn(
        'flex flex-1 flex-col items-start gap-1 rounded-xl border px-4 py-3 text-left transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/30',
        disabled && 'cursor-not-allowed opacity-60',
        active
          ? 'border-accent bg-accent-subtle/40'
          : 'border-border bg-surface hover:border-accent/40 hover:bg-surface-hover',
      )}
    >
      <span className="text-sm font-semibold text-fg">{title}</span>
      <span className="text-xs text-fg-muted">{description}</span>
    </button>
  )
}

export function InputStep({ form, setForm, readonly }: InputStepProps) {
  const updateInput = (mutator: (prev: StructuredSection) => StructuredSection) =>
    setForm((prev) => ({ ...prev, input: mutator(prev.input) }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Tipo de entrada"
          description="Como o usuário fala com o agente. Texto livre é o padrão; estruturado quando o agente precisa receber campos específicos."
        />
        <div className="flex gap-3">
          <ModeCard
            active={form.input.mode === 'text'}
            title="Texto livre"
            description="O usuário envia mensagens em linguagem natural."
            onClick={() => updateInput((prev) => ({ ...prev, mode: 'text' }))}
            disabled={readonly}
          />
          <ModeCard
            active={form.input.mode === 'structured'}
            title="Estruturado"
            description="O agente espera receber um objeto com campos definidos."
            onClick={() => updateInput((prev) => ({ ...prev, mode: 'structured' }))}
            disabled={readonly}
          />
        </div>
      </Card>

      {form.input.mode === 'structured' && (
        <>
          <Card className="space-y-3">
            <CardHeader
              title="Regras do input"
              description="Descreva em linguagem natural o que cada campo significa e como o agente deve interpretar a entrada."
            />
            <Textarea
              value={form.input.description}
              onChange={(e) =>
                updateInput((prev) => ({ ...prev, description: e.target.value }))
              }
              placeholder='Ex: "Espera receber cliente_id (string) e tipo_consulta (enum: pedido, produto, troca)."'
              className="min-h-[100px]"
              disabled={readonly}
            />
          </Card>

          <Card className="space-y-3">
            <CardHeader
              title="Estrutura do input"
              description="Monte os campos esperados. Disponível também em modo JSON cru."
            />
            <JsonSchemaBuilder
              value={form.input.schema}
              onChange={(schema) => updateInput((prev) => ({ ...prev, schema }))}
              emptyHint="Adicione os campos que o agente vai receber."
            />
          </Card>
        </>
      )}
    </div>
  )
}
