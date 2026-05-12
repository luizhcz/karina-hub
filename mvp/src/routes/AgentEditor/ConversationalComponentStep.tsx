import { Card, CardHeader, Input } from '../../ui'
import type { FormState } from './types'

interface ConversationalComponentStepProps {
  form: FormState
  setForm: (mutator: (prev: FormState) => FormState) => void
  readonly: boolean
}

const COMPONENT_MAX_LENGTH = 64

// Step dedicado do Conversational pra escolher o componente único de UI
// que o agente vai emitir no campo `ui_component`. O FormState mantém
// `conversationalUiComponents: string[]` por back-compat com o codec (que
// serializa pra metadata['x-conversational-ui-components'] como JSON array),
// mas a UI agora só permite 1 valor — primeiro item do array.
export function ConversationalComponentStep({
  form,
  setForm,
  readonly,
}: ConversationalComponentStepProps) {
  const current = form.conversationalUiComponents[0] ?? ''

  const setComponent = (value: string) => {
    const trimmed = value.slice(0, COMPONENT_MAX_LENGTH)
    setForm((prev) => ({
      ...prev,
      // Array com 1 elemento (ou vazio quando o user limpa o input) — o codec
      // gera enum de 1 valor no schema canônico `ui_component`. Sem branch
      // pra manter a serialização consistente com agentes legacy.
      conversationalUiComponents: trimmed.trim() ? [trimmed.trim()] : [],
    }))
  }

  return (
    <div className="space-y-5">
      <div className="rounded-lg border border-accent/20 bg-accent/[0.04] p-4 text-sm">
        <h3 className="text-[13px] font-semibold text-fg">Componente de UI</h3>
        <p className="mt-1 text-[12px] leading-relaxed text-fg-muted">
          Identificador único que o agente emite no campo{' '}
          <code className="rounded bg-bg-soft px-1 py-px font-mono text-[11px]">ui_component</code>{' '}
          a cada resposta. O frontend de chat usa esse valor pra escolher o renderer
          (card, lista, texto livre, etc). Use <strong className="text-fg">snake_case</strong> ou{' '}
          <strong className="text-fg">kebab-case</strong>; um identificador por agente.
        </p>
      </div>

      <Card className="space-y-3">
        <CardHeader
          title="Identificador do componente"
          description="Ex.: text, card, order_card, incomplete-form. O codec injeta esse valor como o único do enum `ui_component` no schema canônico { ui_component, message, output }."
        />
        <div>
          <label
            htmlFor="conversational-component"
            className="text-[11px] uppercase tracking-wider text-fg-dim"
          >
            Componente
          </label>
          <Input
            id="conversational-component"
            value={current}
            onChange={(e) => setComponent(e.target.value)}
            placeholder="card"
            disabled={readonly}
            className="mt-1"
            aria-describedby="conversational-component-help"
          />
          <p id="conversational-component-help" className="mt-1.5 text-[11px] text-fg-dim">
            {current
              ? `Saída será dirigida ao renderer "${current}".`
              : 'Defina um identificador antes de salvar para o renderer chat funcionar.'}
          </p>
        </div>
      </Card>
    </div>
  )
}
