import { Card, CardHeader, JsonSchemaBuilder, Textarea, cn } from '../../ui'
import type { FormState, StructuredSection } from './types'

interface OutputStepProps {
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

export function OutputStep({ form, setForm, readonly }: OutputStepProps) {
  const updateOutput = (mutator: (prev: StructuredSection) => StructuredSection) =>
    setForm((prev) => ({ ...prev, output: mutator(prev.output) }))

  return (
    <div className="space-y-5">
      <Card className="space-y-3">
        <CardHeader
          title="Tipo de saída"
          description="Como o agente responde. Texto livre pra conversa natural; estruturado quando outro sistema vai consumir a saída."
        />
        <div className="flex gap-3">
          <ModeCard
            active={form.output.mode === 'text'}
            title="Texto livre"
            description="O agente responde em linguagem natural."
            onClick={() => updateOutput((prev) => ({ ...prev, mode: 'text' }))}
            disabled={readonly}
          />
          <ModeCard
            active={form.output.mode === 'structured'}
            title="Estruturado"
            description="O agente devolve um objeto com campos definidos."
            onClick={() => updateOutput((prev) => ({ ...prev, mode: 'structured' }))}
            disabled={readonly}
          />
        </div>
      </Card>

      {form.output.mode === 'structured' && (
        <>
          <Card className="space-y-3">
            <CardHeader
              title="Regras do output"
              description="Descreva em linguagem natural o que cada campo significa e quando o agente deve preencher."
            />
            <Textarea
              value={form.output.description}
              onChange={(e) =>
                updateOutput((prev) => ({ ...prev, description: e.target.value }))
              }
              placeholder='Ex: "Retorna análise da conversa com sentimento (positivo|neutro|negativo) e resumo curto."'
              className="min-h-[100px]"
              disabled={readonly}
            />
          </Card>

          <Card className="space-y-3">
            <CardHeader
              title="Estrutura do output"
              description="Monte os campos que o agente vai retornar. Disponível também em modo JSON cru."
            />
            <JsonSchemaBuilder
              value={form.output.schema}
              onChange={(schema) => updateOutput((prev) => ({ ...prev, schema }))}
              emptyHint="Adicione os campos que o agente vai retornar."
            />
          </Card>
        </>
      )}
    </div>
  )
}
